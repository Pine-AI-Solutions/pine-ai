using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using PineAI.Core.Features.Safir;
using PineAI.UI.Services;

namespace PineAI.UI.Components.Pages;

public partial class SafirBroadcast : IAsyncDisposable
{
    private const int MaxImages = 10;
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private const int MaxMessageLength = 4096;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    [Inject] private ApiClientService ApiClient { get; set; } = default!;
    [Inject] private NotificationService NotificationService { get; set; } = default!;

    private int? recipientCount;
    private bool isConfigured = true;

    private string messageText = string.Empty;
    private readonly List<SelectedImage> images = new();
    private Guid inputKey = Guid.NewGuid();

    private bool showConfirm;
    private bool isSending;
    private bool isCancelling;
    private bool isResuming;

    private List<SafirBroadcastStatusDto> unfinished = new();

    private SafirBroadcastStatusDto? status;
    private CancellationTokenSource? pollCts;

    private bool IsBroadcastActive => status?.IsActive == true;

    /// <summary>Unfinished jobs other than the one already shown in the status card.</summary>
    private List<SafirBroadcastStatusDto> UnfinishedOthers =>
        unfinished.Where(j => j.JobId != status?.JobId).ToList();

    private bool CanSend =>
        isConfigured
        && !isSending
        && !IsBroadcastActive
        && recipientCount is > 0
        && (!string.IsNullOrWhiteSpace(messageText) || images.Count > 0);

    private int Percent =>
        status is null || status.TotalRecipients == 0
            ? 0
            : (int)Math.Round(status.ProcessedRecipients * 100.0 / status.TotalRecipients);

    protected override async Task OnInitializedAsync()
    {
        var info = await ApiClient.GetSafirRecipientsAsync();
        if (info != null)
        {
            recipientCount = info.RecipientCount;
            isConfigured = info.IsConfigured;
        }
        else
        {
            NotificationService.ShowError("خطا در دریافت اطلاعات مخاطبین");
        }

        unfinished = await ApiClient.GetUnfinishedSafirBroadcastsAsync();

        // Show an ongoing / the most recent broadcast again after a page reload.
        var latest = await ApiClient.GetLatestSafirBroadcastAsync();
        if (latest != null)
        {
            status = latest;
            if (latest.IsActive)
                StartPolling(latest.JobId);
        }
    }

    private async Task HandleFilesSelected(InputFileChangeEventArgs e)
    {
        try
        {
            foreach (var file in e.GetMultipleFiles(Math.Max(1, e.FileCount)))
            {
                if (images.Count >= MaxImages)
                {
                    NotificationService.ShowWarning($"حداکثر {MaxImages} تصویر مجاز است");
                    break;
                }

                var contentType = ResolveContentType(file);
                if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    NotificationService.ShowWarning($"فایل «{file.Name}» تصویر نیست و نادیده گرفته شد");
                    continue;
                }

                if (file.Size > MaxImageBytes)
                {
                    NotificationService.ShowWarning($"حجم «{file.Name}» بیشتر از {MaxImageBytes / 1024 / 1024} مگابایت است");
                    continue;
                }

                using var ms = new MemoryStream();
                await using (var stream = file.OpenReadStream(MaxImageBytes))
                    await stream.CopyToAsync(ms);

                images.Add(new SelectedImage
                {
                    Name = file.Name,
                    ContentType = contentType,
                    Content = ms.ToArray(),
                    PreviewDataUrl = await BuildPreviewAsync(file)
                });
            }
        }
        catch (Exception ex)
        {
            NotificationService.ShowError($"خطا در خواندن تصویر: {ex.Message}");
        }
        finally
        {
            // New key => fresh <InputFile>, so the same file can be picked again after removing it.
            inputKey = Guid.NewGuid();
        }
    }

    /// <summary>Small client-side resized thumbnail, so previews don't push full-size images through SignalR.</summary>
    private static async Task<string?> BuildPreviewAsync(IBrowserFile file)
    {
        try
        {
            var thumbnail = await file.RequestImageFileAsync("image/jpeg", 160, 160);
            using var ms = new MemoryStream();
            await using (var stream = thumbnail.OpenReadStream(1024 * 1024))
                await stream.CopyToAsync(ms);
            return $"data:image/jpeg;base64,{Convert.ToBase64String(ms.ToArray())}";
        }
        catch
        {
            return null; // preview is optional (e.g. format the browser cannot decode)
        }
    }

    private static string ResolveContentType(IBrowserFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.ContentType))
            return file.ContentType;

        return Path.GetExtension(file.Name).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private void RemoveImage(SelectedImage image) => images.Remove(image);

    private void RequestSend()
    {
        if (string.IsNullOrWhiteSpace(messageText) && images.Count == 0)
        {
            NotificationService.ShowWarning("متن پیام یا حداقل یک تصویر الزامی است");
            return;
        }

        showConfirm = true;
    }

    private async Task ConfirmSendAsync()
    {
        isSending = true;
        try
        {
            var result = await ApiClient.StartSafirBroadcastAsync(
                messageText.Trim(),
                images.Select(i => new SafirImageUpload(i.Name, i.ContentType, i.Content)));

            if (!result.Success || string.IsNullOrEmpty(result.JobId))
            {
                NotificationService.ShowError(result.Message);
                return;
            }

            NotificationService.ShowSuccess($"ارسال برای {result.TotalRecipients} مشتری شروع شد");

            messageText = string.Empty;
            images.Clear();

            status = new SafirBroadcastStatusDto
            {
                JobId = result.JobId,
                State = SafirBroadcastJobState.Queued,
                TotalRecipients = result.TotalRecipients
            };
            StartPolling(result.JobId);
        }
        finally
        {
            isSending = false;
            showConfirm = false;
        }
    }

    private async Task CancelAsync()
    {
        if (status is null) return;

        isCancelling = true;
        try
        {
            if (await ApiClient.CancelSafirBroadcastAsync(status.JobId))
                NotificationService.ShowInformation("درخواست توقف ارسال ثبت شد؛ بعداً می‌توانید ارسال را ادامه دهید");
            else
                NotificationService.ShowError("توقف ارسال انجام نشد");
        }
        finally
        {
            isCancelling = false;
        }
    }

    private async Task ResumeAsync(string jobId)
    {
        isResuming = true;
        try
        {
            var (resumed, error) = await ApiClient.ResumeSafirBroadcastAsync(jobId);
            if (resumed is null)
            {
                NotificationService.ShowError(error ?? "ادامه ارسال انجام نشد");
                return;
            }

            NotificationService.ShowSuccess($"ادامه ارسال شروع شد؛ {resumed.RemainingRecipients} مخاطب باقی‌مانده");
            status = resumed;
            unfinished = await ApiClient.GetUnfinishedSafirBroadcastsAsync();
            StartPolling(resumed.JobId);
        }
        finally
        {
            isResuming = false;
        }
    }

    private async Task DeleteAsync(string jobId)
    {
        var (success, error) = await ApiClient.DeleteSafirBroadcastAsync(jobId);
        if (!success)
        {
            NotificationService.ShowError(error ?? "حذف انجام نشد");
            return;
        }

        if (status?.JobId == jobId)
            status = null;

        unfinished = await ApiClient.GetUnfinishedSafirBroadcastsAsync();
        NotificationService.ShowSuccess("ارسال ناتمام حذف شد");
    }

    private void StartPolling(string jobId)
    {
        pollCts?.Cancel();
        pollCts?.Dispose();
        pollCts = new CancellationTokenSource();
        _ = PollAsync(jobId, pollCts.Token);
    }

    private async Task PollAsync(string jobId, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var latest = await ApiClient.GetSafirBroadcastAsync(jobId);

                if (latest != null)
                {
                    var wasActive = status?.IsActive ?? true;
                    status = latest;
                    await InvokeAsync(StateHasChanged);

                    if (!latest.IsActive)
                    {
                        unfinished = await ApiClient.GetUnfinishedSafirBroadcastsAsync();
                        if (wasActive)
                            await InvokeAsync(() => NotifyFinished(latest));
                        await InvokeAsync(StateHasChanged);
                        return;
                    }
                }

                await Task.Delay(PollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // page left or a newer poll replaced this one
        }
    }

    private void NotifyFinished(SafirBroadcastStatusDto job)
    {
        switch (job.State)
        {
            case SafirBroadcastJobState.Completed:
                NotificationService.ShowSuccess($"ارسال تمام شد: {job.SuccessCount} موفق، {job.FailedCount} ناموفق");
                break;
            case SafirBroadcastJobState.Cancelled:
                NotificationService.ShowWarning("ارسال متوقف شد؛ می‌توانید آن را ادامه دهید");
                break;
            default:
                NotificationService.ShowError(job.Message ?? "ارسال ناموفق بود");
                break;
        }
    }

    private static string StateText(SafirBroadcastJobState state) => state switch
    {
        SafirBroadcastJobState.Queued => "در صف",
        SafirBroadcastJobState.Running => "در حال ارسال",
        SafirBroadcastJobState.Completed => "تکمیل شد",
        SafirBroadcastJobState.Failed => "ناموفق",
        SafirBroadcastJobState.Cancelled => "متوقف‌شده",
        SafirBroadcastJobState.Interrupted => "متوقف‌شده",
        _ => state.ToString()
    };

    private static string StateBadgeClass(SafirBroadcastJobState state) => state switch
    {
        SafirBroadcastJobState.Queued => "bg-secondary",
        SafirBroadcastJobState.Running => "bg-primary",
        SafirBroadcastJobState.Completed => "bg-success",
        SafirBroadcastJobState.Failed => "bg-danger",
        SafirBroadcastJobState.Cancelled => "bg-warning text-dark",
        SafirBroadcastJobState.Interrupted => "bg-warning text-dark",
        _ => "bg-secondary"
    };

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:F1} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    public ValueTask DisposeAsync()
    {
        pollCts?.Cancel();
        pollCts?.Dispose();
        pollCts = null;
        return ValueTask.CompletedTask;
    }

    private sealed class SelectedImage
    {
        public string Name { get; init; } = string.Empty;
        public string ContentType { get; init; } = string.Empty;
        public byte[] Content { get; init; } = Array.Empty<byte>();
        public string? PreviewDataUrl { get; init; }
    }
}
