using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PineAI.Core.Contracts;
using PineAI.Core.Features.Safir;
using System.Security.Claims;

namespace PineAI.Api.Controllers;

/// <summary>
/// Defines Safir broadcasts (one message, text and/or images, to every distinct customer phone number) and shows
/// their progress. A job is only saved in the database here; PineAI.SafirSender picks it up and sends it.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
public class SafirController : ControllerBase
{
    private const int MaxMessageLength = 4096;
    private const int MaxImageCount = 10;
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private const long MaxRequestBytes = MaxImageCount * MaxImageBytes + 1024 * 1024;

    private const string NotConfiguredMessage = "تنظیمات سفیر (BaleMessenger:Safir و BotId) در سرور انجام نشده است";
    private const string NotFoundMessage = "ارسال مورد نظر یافت نشد";
    private const string BusyMessage = "یک ارسال دیگر در حال انجام است. لطفاً تا پایان آن صبر کنید";

    private readonly ICustomerService customerService;
    private readonly ISafirBroadcastService broadcasts;
    private readonly IConfiguration configuration;
    private readonly ILogger<SafirController> logger;

    public SafirController(
        ICustomerService customerService,
        ISafirBroadcastService broadcasts,
        IConfiguration configuration,
        ILogger<SafirController> logger)
    {
        this.customerService = customerService;
        this.broadcasts = broadcasts;
        this.configuration = configuration;
        this.logger = logger;
    }

    private bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["BaleMessenger:Safir"]) &&
        long.TryParse(configuration["BaleMessenger:BotId"], out var botId) && botId > 0;

    /// <summary>How many distinct customer phone numbers a broadcast would reach, and whether Safir is configured.</summary>
    [HttpGet("recipients")]
    public async Task<ActionResult<SafirRecipientsInfoDto>> GetRecipients()
    {
        var phones = await customerService.GetDistinctPhoneNumbers();
        return Ok(new SafirRecipientsInfoDto
        {
            RecipientCount = phones.Count,
            IsConfigured = IsConfigured
        });
    }

    /// <summary>
    /// Defines a broadcast. Multipart form: <c>message</c> (text, optional when images exist)
    /// and <c>images</c> (0..10 image files, max 5 MB each).
    /// </summary>
    [HttpPost("broadcast")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<IActionResult> StartBroadcast(CancellationToken ct)
    {
        if (!IsConfigured)
            return BadRequest(new { message = NotConfiguredMessage });

        if (await broadcasts.HasActiveJobAsync(ct))
            return Conflict(new { message = BusyMessage });

        var form = await Request.ReadFormAsync(ct);

        var text = form["message"].ToString().Trim();
        var files = form.Files.GetFiles("images");

        if (text.Length == 0 && files.Count == 0)
            return BadRequest(new { message = "متن پیام یا حداقل یک تصویر الزامی است" });

        if (text.Length > MaxMessageLength)
            return BadRequest(new { message = $"طول پیام نباید بیشتر از {MaxMessageLength} کاراکتر باشد" });

        if (files.Count > MaxImageCount)
            return BadRequest(new { message = $"حداکثر {MaxImageCount} تصویر مجاز است" });

        var images = new List<SafirImageData>();
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file.FileName);

            if (file.Length == 0)
                return BadRequest(new { message = $"فایل «{fileName}» خالی است" });

            if (file.Length > MaxImageBytes)
                return BadRequest(new { message = $"حجم فایل «{fileName}» بیشتر از {MaxImageBytes / 1024 / 1024} مگابایت است" });

            if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = $"فایل «{fileName}» تصویر نیست" });

            using var ms = new MemoryStream((int)file.Length);
            await file.CopyToAsync(ms, ct);
            images.Add(new SafirImageData(fileName, file.ContentType, ms.ToArray()));
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

        // Saved in the database (message, images, recipients) so the sender can run it and resume it after any interruption.
        var (jobId, total) = await broadcasts.CreateAsync(text.Length == 0 ? null : text, images, userId, ct);
        if (total == 0)
            return BadRequest(new { message = "هیچ شماره مشتری برای ارسال وجود ندارد" });

        logger.LogInformation("Safir broadcast {JobId} queued by user {UserId}: {Recipients} recipients, {Images} image(s), text length {Length}",
            jobId, userId, total, images.Count, text.Length);

        return Ok(new StartSafirBroadcastResult
        {
            Success = true,
            Message = "ارسال شروع شد",
            JobId = jobId,
            TotalRecipients = total
        });
    }

    /// <summary>Live progress of a broadcast job (read from the database, written by the sender).</summary>
    [HttpGet("broadcast/{jobId}")]
    public async Task<ActionResult<SafirBroadcastStatusDto>> GetBroadcast(string jobId, CancellationToken ct)
    {
        var status = await broadcasts.GetStatusAsync(jobId, ct);
        if (status == null) return NotFound(new { message = NotFoundMessage });
        return Ok(status);
    }

    /// <summary>Task log of a broadcast (kept in the database), oldest first.</summary>
    [HttpGet("broadcast/{jobId}/logs")]
    public async Task<ActionResult<List<SafirBroadcastLogDto>>> GetBroadcastLogs(string jobId, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        return Ok(await broadcasts.GetLogsAsync(jobId, Math.Clamp(take, 1, 500), ct));
    }

    /// <summary>Most recent broadcast (so the page can show progress again after a reload). 204 when none exists.</summary>
    [HttpGet("broadcast/latest")]
    public async Task<ActionResult<SafirBroadcastStatusDto>> GetLatestBroadcast(CancellationToken ct)
    {
        var status = await broadcasts.GetLatestStatusAsync(ct);
        if (status == null) return NoContent();
        return Ok(status);
    }

    /// <summary>Broadcasts that stopped before reaching everyone (failed, cancelled, interrupted by a restart) and can be resumed.</summary>
    [HttpGet("broadcast/unfinished")]
    public async Task<ActionResult<List<SafirBroadcastStatusDto>>> GetUnfinishedBroadcasts(CancellationToken ct)
    {
        return Ok(await broadcasts.GetResumableAsync(ct));
    }

    /// <summary>
    /// Continues an unfinished broadcast. Recipients who already received the message (or can never receive it)
    /// are skipped; the rest, including temporary failures, are sent again.
    /// </summary>
    [HttpPost("broadcast/{jobId}/resume")]
    public async Task<IActionResult> ResumeBroadcast(string jobId, CancellationToken ct)
    {
        if (!IsConfigured)
            return BadRequest(new { message = NotConfiguredMessage });

        var error = await broadcasts.ResumeAsync(jobId, ct);
        if (error != null)
            return error == NotFoundMessage ? NotFound(new { message = error }) : BadRequest(new { message = error });

        logger.LogInformation("Safir broadcast {JobId} resumed by user {UserId}", jobId,
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value);

        return Ok(await broadcasts.GetStatusAsync(jobId, ct));
    }

    /// <summary>Stops a running broadcast. Messages that were already sent cannot be recalled; it can be resumed later.</summary>
    [HttpPost("broadcast/{jobId}/cancel")]
    public async Task<IActionResult> CancelBroadcast(string jobId, CancellationToken ct)
    {
        var error = await broadcasts.CancelAsync(jobId, ct);
        if (error != null)
            return error == NotFoundMessage ? NotFound(new { message = error }) : BadRequest(new { message = error });

        return Ok(new { message = "درخواست لغو ثبت شد" });
    }

    /// <summary>Deletes a finished/stopped broadcast and everything stored for it. Not possible while it runs.</summary>
    [HttpDelete("broadcast/{jobId}")]
    public async Task<IActionResult> DeleteBroadcast(string jobId, CancellationToken ct)
    {
        var error = await broadcasts.DeleteAsync(jobId, ct);
        if (error != null)
            return error == NotFoundMessage ? NotFound(new { message = error }) : BadRequest(new { message = error });

        return Ok(new { message = "حذف شد" });
    }
}
