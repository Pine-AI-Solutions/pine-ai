using Microsoft.Extensions.Options;
using PineAI.Core.Contracts;
using PineAI.Core.Features.Safir;
using PineAI.SafirSender.Services;

namespace PineAI.SafirSender;

/// <summary>
/// Picks Safir broadcasts queued in the database (new ones and resumed ones) and sends them one after another.
/// All progress is written to the database after every recipient, so after a crash or a stop the job continues
/// with the recipients that are still pending and nobody receives the message twice.
/// </summary>
public sealed class SafirBroadcastWorker(
    IServiceScopeFactory scopeFactory,
    ISafirClient safir,
    IOptions<SafirSettings> options,
    ILogger<SafirBroadcastWorker> logger) : BackgroundService
{
    private const int MaxRateLimitRetries = 4;
    private const int ProgressLogEvery = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<ISafirBroadcastService>();
                await service.RecoverAsync(options.Value.BroadcastRetentionDays, stoppingToken);
            }

            logger.LogInformation("Safir sender started; waiting for queued broadcasts");

            while (!stoppingToken.IsCancellationRequested)
            {
                var worked = false;

                try
                {
                    worked = await RunNextJobAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Safir sender loop error");
                }

                if (!worked)
                    await Task.Delay(PollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task<bool> RunNextJobAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISafirBroadcastService>();

        var job = await service.ClaimNextAsync(stoppingToken);
        if (job is null) return false;

        try
        {
            await ProcessAsync(service, job.Id, job.Text, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown: the job stays resumable.
            await service.FinishAsync(job.Id, SafirBroadcastJobState.Interrupted,
                "سرویس متوقف شد؛ می‌توانید ارسال را ادامه دهید");
            await service.LogAsync(job.Id, "Warning", "ارسال با توقف سرویس نیمه‌کاره ماند");
            logger.LogWarning("Safir broadcast {JobId} interrupted by shutdown", job.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in Safir broadcast {JobId}", job.Id);
            await service.FinishAsync(job.Id, SafirBroadcastJobState.Failed, "خطای غیرمنتظره در ارسال: " + ex.Message);
            await service.LogAsync(job.Id, "Error", "خطای غیرمنتظره: " + ex.Message);
        }

        return true;
    }

    private async Task ProcessAsync(ISafirBroadcastService service, string jobId, string? text, CancellationToken ct)
    {
        // Recipients who already received the message (or can never receive it) are not pending, which is what makes resume safe.
        var pending = await service.GetPendingPhonesAsync(jobId, ct);
        var images = await service.GetImagesAsync(jobId, ct);

        await service.LogAsync(jobId, "Information",
            $"شروع ارسال: {pending.Count} مخاطب در انتظار، {images.Count} تصویر", ct: ct);
        logger.LogInformation("Safir broadcast {JobId} running: {Pending} recipients pending, {Images} image(s)",
            jobId, pending.Count, images.Count);

        if (pending.Count == 0)
        {
            await FinishAsCompletedAsync(service, jobId);
            return;
        }

        // 1) Upload every image once; the returned file_id is reused for all recipients.
        var fileIds = new List<string>();
        foreach (var image in images)
        {
            var upload = await safir.UploadFileAsync(image.Content, image.FileName, image.ContentType, ct);
            if (!upload.Success || string.IsNullOrWhiteSpace(upload.FileId))
            {
                var error = $"آپلود تصویر «{image.FileName}» در سفیر ناموفق بود: {upload.Error}";
                await service.FinishAsync(jobId, SafirBroadcastJobState.Failed, error);
                await service.LogAsync(jobId, "Error", error);
                return;
            }
            fileIds.Add(upload.FileId!);
        }

        // 2) Send to every pending phone number.
        var delay = TimeSpan.FromMilliseconds(Math.Max(0, options.Value.BroadcastDelayMs));
        var done = 0;

        var batchSize = options.Value.TesterBatchSize;
        var testers = new List<string>();
        var alreadyProcessed = 0;
        if (batchSize > 0)
        {
            using var scope = scopeFactory.CreateScope();
            testers = await scope.ServiceProvider.GetRequiredService<ICustomerService>().GetTesterPhoneNumbers();
            alreadyProcessed = (await service.GetStatusAsync(jobId, ct))?.TotalRecipients - pending.Count ?? 0;
        }

        foreach (var phone in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (await service.HeartbeatAsync(jobId, ct))
            {
                await service.FinishAsync(jobId, SafirBroadcastJobState.Cancelled,
                    "ارسال توسط کاربر متوقف شد؛ می‌توانید آن را ادامه دهید");
                await service.LogAsync(jobId, "Warning", $"ارسال توسط کاربر متوقف شد ({done}/{pending.Count} در این نوبت)");
                logger.LogWarning("Safir broadcast {JobId} cancelled by user after {Done}/{Pending}", jobId, done, pending.Count);
                return;
            }

            // Testers get the same message before the first number and before every following batch.
            var position = alreadyProcessed + done;
            if (batchSize > 0 && position % batchSize == 0 && testers.Count > 0)
                await SendToTestersAsync(service, jobId, text, testers, fileIds, delay, position, ct);

            var fatal = await SendToRecipientAsync(service, jobId, text, phone, fileIds, delay, ct);
            if (fatal is not null)
            {
                await service.FinishAsync(jobId, SafirBroadcastJobState.Failed, "ارسال متوقف شد: " + fatal);
                await service.LogAsync(jobId, "Error", "ارسال متوقف شد: " + fatal);
                logger.LogError("Safir broadcast {JobId} aborted: {Reason}", jobId, fatal);
                return;
            }

            if (++done % ProgressLogEvery == 0)
                await service.LogAsync(jobId, "Information", $"پیشرفت: {done} از {pending.Count} مخاطب پردازش شد", ct: ct);
        }

        await FinishAsCompletedAsync(service, jobId);
    }

    /// <summary>Sends the broadcast message to tester customers. Failures are only logged; they never stop the job.</summary>
    private async Task SendToTestersAsync(
        ISafirBroadcastService service, string jobId, string? text, List<string> testers,
        List<string> fileIds, TimeSpan delay, int position, CancellationToken ct)
    {
        await service.LogAsync(jobId, "Information",
            $"ارسال پیام به {testers.Count} مشتری آزمایشی قبل از مخاطب شماره {position + 1}", ct: ct);

        var messages = fileIds.Count == 0
            ? new List<(string? Text, string? FileId)> { (text, null) }
            : fileIds.Select((id, index) => (index == 0 ? text : null, (string?)id)).ToList();

        foreach (var tester in testers)
        {
            for (var index = 0; index < messages.Count; index++)
            {
                var (messageText, fileId) = messages[index];
                var requestId = $"{jobId[..12]}-T{position}-{tester}-{index}";

                var result = await SendWithRetryAsync(tester, messageText, fileId, requestId, ct);
                if (!result.Success)
                {
                    await service.LogAsync(jobId, "Warning", "ارسال به مشتری آزمایشی ناموفق بود: " + result.Error, tester, ct);
                    break;
                }

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);
            }
        }
    }

    private async Task FinishAsCompletedAsync(ISafirBroadcastService service, string jobId)
    {
        await service.FinishAsync(jobId, SafirBroadcastJobState.Completed);

        var status = await service.GetStatusAsync(jobId);
        await service.LogAsync(jobId, "Information",
            $"ارسال پایان یافت: {status?.SuccessCount} موفق، {status?.FailedCount} ناموفق از {status?.TotalRecipients}");
        logger.LogInformation("Safir broadcast {JobId} finished: {Success} sent, {Failed} failed of {Total}",
            jobId, status?.SuccessCount, status?.FailedCount, status?.TotalRecipients);

        // Nothing left to resume => the images are no longer needed.
        if (status is { CanResume: false })
            await service.DeleteImagesAsync(jobId);
    }

    /// <summary>Returns a reason when the whole broadcast must stop (e.g. no credit); otherwise null.</summary>
    private async Task<string?> SendToRecipientAsync(
        ISafirBroadcastService service, string jobId, string? text, string phone,
        List<string> fileIds, TimeSpan delay, CancellationToken ct)
    {
        // No images: one text message. With images: one message per image, the text is the caption of the first one.
        var messages = fileIds.Count == 0
            ? new List<(string? Text, string? FileId)> { (text, null) }
            : fileIds.Select((id, index) => (index == 0 ? text : null, (string?)id)).ToList();

        for (var index = 0; index < messages.Count; index++)
        {
            var (messageText, fileId) = messages[index];

            // Stable per job + phone + position, so a resumed run re-uses the same request_id and Safir
            // can ignore a message it already delivered just before an interruption.
            var requestId = $"{jobId[..12]}-{phone}-{index}";

            var result = await SendWithRetryAsync(phone, messageText, fileId, requestId, ct);

            if (!result.Success)
            {
                // A fatal error (no credit / bad key) says nothing about this phone, so it stays pending.
                if (result.IsFatal)
                    return result.Error ?? "خطای نامشخص";

                await service.RecordFailureAsync(jobId, phone, result.Error ?? "خطای نامشخص",
                    result.IsPermanentForRecipient, ct);
                return null;
            }

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
        }

        await service.RecordSuccessAsync(jobId, phone, ct);
        return null;
    }

    private async Task<SafirSendResult> SendWithRetryAsync(
        string phone, string? text, string? fileId, string requestId, CancellationToken ct)
    {
        SafirSendResult result = null!;

        for (var attempt = 1; attempt <= MaxRateLimitRetries; attempt++)
        {
            result = await safir.SendMessageAsync(phone, text, fileId, requestId, ct);

            if (result.Success || !result.IsRateLimited)
                return result;

            await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
        }

        return result;
    }
}
