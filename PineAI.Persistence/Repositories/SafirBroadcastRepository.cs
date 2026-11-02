using Microsoft.EntityFrameworkCore;
using PineAI.Core.Contracts;
using PineAI.Core.Entities;
using PineAI.Core.Features.Safir;
using PineAI.Persistence.Services;

namespace PineAI.Persistence.Repositories;

public sealed class SafirBroadcastRepository(PineAIDbContext db) : ISafirBroadcastService
{
    private const int MaxRecentErrors = 50;
    private const int MaxTextPreview = 80;

    private static readonly SafirBroadcastJobState[] ActiveStates =
        [SafirBroadcastJobState.Queued, SafirBroadcastJobState.Running];

    // ── API side ───────────────────────────────────────────────────────────

    public Task<bool> HasActiveJobAsync(CancellationToken ct = default) =>
        db.Set<SafirBroadcast>().AnyAsync(j => ActiveStates.Contains(j.State), ct);

    public async Task<(string JobId, int TotalRecipients)> CreateAsync(string? text, IReadOnlyList<SafirImageData> images,
        string createdByUserId, CancellationToken ct = default)
    {
        var job = new SafirBroadcast
        {
            Text = text,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.Now,
            // Hidden from the sender until the recipients exist.
            State = SafirBroadcastJobState.Cancelled
        };

        for (var i = 0; i < images.Count; i++)
            job.Images.Add(new SafirBroadcastImage
            {
                Position = i,
                FileName = images[i].FileName,
                ContentType = images[i].ContentType,
                Content = images[i].Content
            });

        db.Add(job);
        await db.SaveChangesAsync(ct);

        var id = job.Id;
        db.ChangeTracker.Clear();

        // One set-based insert straight from the customer table (no entity per recipient).
        var total = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO SafirBroadcastRecipient (BroadcastId, Phone, Status, Attempts)
            SELECT DISTINCT {id}, PhoneNumber, 0, 0 FROM Customer
            WHERE PhoneNumber IS NOT NULL AND PhoneNumber <> ''", ct);

        if (total == 0)
        {
            await DeleteJobsAsync(j => j.Id == id, ct);
            return (id, 0);
        }

        await db.Set<SafirBroadcast>().Where(j => j.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, SafirBroadcastJobState.Queued), ct);

        await LogAsync(id, "Information", $"ارسال ثبت شد: {total} مخاطب، {images.Count} تصویر", ct: ct);
        return (id, total);
    }

    public async Task<SafirBroadcastStatusDto?> GetStatusAsync(string jobId, CancellationToken ct = default)
    {
        var job = await db.Set<SafirBroadcast>().AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        return job is null ? null : await BuildDtoAsync(job, ct);
    }

    public async Task<SafirBroadcastStatusDto?> GetLatestStatusAsync(CancellationToken ct = default)
    {
        var job = await db.Set<SafirBroadcast>().AsNoTracking()
            .OrderByDescending(j => j.CreatedAt).FirstOrDefaultAsync(ct);
        return job is null ? null : await BuildDtoAsync(job, ct);
    }

    public async Task<List<SafirBroadcastStatusDto>> GetResumableAsync(CancellationToken ct = default)
    {
        var jobs = await db.Set<SafirBroadcast>().AsNoTracking()
            .Where(j => !ActiveStates.Contains(j.State) && j.State != SafirBroadcastJobState.Completed)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);

        var result = new List<SafirBroadcastStatusDto>();
        foreach (var job in jobs)
        {
            var dto = await BuildDtoAsync(job, ct);
            if (dto.CanResume) result.Add(dto);
        }
        return result;
    }

    public async Task<List<SafirBroadcastLogDto>> GetLogsAsync(string jobId, int take, CancellationToken ct = default)
    {
        var logs = await db.Set<SafirBroadcastLog>().AsNoTracking()
            .Where(l => l.BroadcastId == jobId)
            .OrderByDescending(l => l.Id)
            .Take(take)
            .ToListAsync(ct);

        return logs.Select(l => new SafirBroadcastLogDto(l.At, l.Level, l.Phone, l.Message)).Reverse().ToList();
    }

    public async Task<string?> ResumeAsync(string jobId, CancellationToken ct = default)
    {
        var job = await db.Set<SafirBroadcast>().AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return "ارسال مورد نظر یافت نشد";
        if (ActiveStates.Contains(job.State)) return "این ارسال هم‌اکنون در حال انجام است";
        if (!await HasRemainingAsync(jobId, ct)) return "برای این ارسال مخاطب باقی‌مانده‌ای وجود ندارد";
        if (await db.Set<SafirBroadcast>().AnyAsync(j => ActiveStates.Contains(j.State), ct))
            return "یک ارسال دیگر در حال انجام است. لطفاً تا پایان آن صبر کنید";

        // Temporary failures are tried again; everyone already sent (or permanently failed) is never touched.
        await db.Set<SafirBroadcastRecipient>()
            .Where(r => r.BroadcastId == jobId && r.Status == SafirRecipientStatus.FailedRetryable)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, SafirRecipientStatus.Pending)
                .SetProperty(r => r.Error, (string?)null), ct);

        await db.Set<SafirBroadcast>().Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, SafirBroadcastJobState.Queued)
                .SetProperty(j => j.Message, (string?)null)
                .SetProperty(j => j.FinishedAt, (DateTime?)null)
                .SetProperty(j => j.CancelRequested, false), ct);

        await LogAsync(jobId, "Information", "ارسال برای ادامه دوباره در صف قرار گرفت", ct: ct);
        return null;
    }

    public async Task<string?> CancelAsync(string jobId, CancellationToken ct = default)
    {
        var job = await db.Set<SafirBroadcast>().AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return "ارسال مورد نظر یافت نشد";
        if (!ActiveStates.Contains(job.State)) return "این ارسال دیگر فعال نیست";

        // Still waiting in the queue: stop it right away. Otherwise the sender sees the flag and stops.
        var cancelledDirectly = await db.Set<SafirBroadcast>()
            .Where(j => j.Id == jobId && j.State == SafirBroadcastJobState.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, SafirBroadcastJobState.Cancelled)
                .SetProperty(j => j.Message, "ارسال توسط کاربر متوقف شد؛ می‌توانید آن را ادامه دهید")
                .SetProperty(j => j.FinishedAt, DateTime.Now), ct);

        if (cancelledDirectly == 0)
            await db.Set<SafirBroadcast>().Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.CancelRequested, true), ct);

        await LogAsync(jobId, "Warning", "درخواست لغو توسط کاربر ثبت شد", ct: ct);
        return null;
    }

    public async Task<string?> DeleteAsync(string jobId, CancellationToken ct = default)
    {
        var job = await db.Set<SafirBroadcast>().AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return "ارسال مورد نظر یافت نشد";
        if (ActiveStates.Contains(job.State)) return "ارسال در حال انجام را نمی‌توان حذف کرد؛ ابتدا آن را لغو کنید";

        await DeleteJobsAsync(j => j.Id == jobId, ct);
        return null;
    }

    // ── sender side ────────────────────────────────────────────────────────

    public async Task RecoverAsync(int retentionDays, CancellationToken ct = default)
    {
        // Only one sender runs, so a job still marked Running was cut off by a stop / crash.
        var interrupted = await db.Set<SafirBroadcast>().Where(j => j.State == SafirBroadcastJobState.Running)
            .Select(j => j.Id).ToListAsync(ct);

        foreach (var id in interrupted)
        {
            await FinishAsync(id, SafirBroadcastJobState.Interrupted,
                "سرویس در حین ارسال متوقف شد؛ می‌توانید ارسال را ادامه دهید", ct);
            await LogAsync(id, "Warning", "ارسال به دلیل توقف سرویس نیمه‌کاره ماند", ct: ct);
        }

        if (retentionDays > 0)
        {
            var limit = DateTime.Now.AddDays(-retentionDays);
            await DeleteJobsAsync(j => !ActiveStates.Contains(j.State) && (j.FinishedAt ?? j.CreatedAt) < limit, ct);
        }
    }

    public async Task<SafirBroadcast?> ClaimNextAsync(CancellationToken ct = default)
    {
        var id = await db.Set<SafirBroadcast>().AsNoTracking()
            .Where(j => j.State == SafirBroadcastJobState.Queued)
            .OrderBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .FirstOrDefaultAsync(ct);

        if (id is null) return null;

        var now = DateTime.Now;
        var claimed = await db.Set<SafirBroadcast>()
            .Where(j => j.Id == id && j.State == SafirBroadcastJobState.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, SafirBroadcastJobState.Running)
                .SetProperty(j => j.Message, (string?)null)
                .SetProperty(j => j.StartedAt, now)
                .SetProperty(j => j.LastActivityAt, now), ct);

        if (claimed == 0) return null;

        return await db.Set<SafirBroadcast>().AsNoTracking().FirstAsync(j => j.Id == id, ct);
    }

    public async Task<List<SafirImageData>> GetImagesAsync(string jobId, CancellationToken ct = default)
    {
        var images = await db.Set<SafirBroadcastImage>().AsNoTracking()
            .Where(i => i.BroadcastId == jobId).OrderBy(i => i.Position).ToListAsync(ct);

        return images.Select(i => new SafirImageData(i.FileName, i.ContentType, i.Content)).ToList();
    }

    public Task<List<string>> GetPendingPhonesAsync(string jobId, CancellationToken ct = default) =>
        db.Set<SafirBroadcastRecipient>().AsNoTracking()
            .Where(r => r.BroadcastId == jobId && r.Status == SafirRecipientStatus.Pending)
            .OrderBy(r => r.Id)
            .Select(r => r.Phone)
            .ToListAsync(ct);

    public Task RecordSuccessAsync(string jobId, string phone, CancellationToken ct = default) =>
        db.Set<SafirBroadcastRecipient>().Where(r => r.BroadcastId == jobId && r.Phone == phone)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, SafirRecipientStatus.Sent)
                .SetProperty(r => r.Error, (string?)null)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1)
                .SetProperty(r => r.ProcessedAt, DateTime.Now), ct);

    public async Task RecordFailureAsync(string jobId, string phone, string reason, bool permanent, CancellationToken ct = default)
    {
        reason = OneLine(reason);
        var status = permanent ? SafirRecipientStatus.FailedPermanent : SafirRecipientStatus.FailedRetryable;

        await db.Set<SafirBroadcastRecipient>().Where(r => r.BroadcastId == jobId && r.Phone == phone)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.Error, reason)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1)
                .SetProperty(r => r.ProcessedAt, DateTime.Now), ct);

        await LogAsync(jobId, "Warning", reason, phone, ct);
    }

    public async Task<bool> HeartbeatAsync(string jobId, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        await db.Set<SafirBroadcast>().Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LastActivityAt, now), ct);

        return await db.Set<SafirBroadcast>().AsNoTracking()
            .Where(j => j.Id == jobId).Select(j => j.CancelRequested).FirstOrDefaultAsync(ct);
    }

    public Task FinishAsync(string jobId, SafirBroadcastJobState state, string? message = null, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        return db.Set<SafirBroadcast>().Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.State, state)
                .SetProperty(j => j.Message, message)
                .SetProperty(j => j.FinishedAt, now)
                .SetProperty(j => j.LastActivityAt, now)
                .SetProperty(j => j.CancelRequested, false), ct);
    }

    public Task<bool> HasRemainingAsync(string jobId, CancellationToken ct = default) =>
        db.Set<SafirBroadcastRecipient>().AnyAsync(r => r.BroadcastId == jobId &&
            (r.Status == SafirRecipientStatus.Pending || r.Status == SafirRecipientStatus.FailedRetryable), ct);

    public Task DeleteImagesAsync(string jobId, CancellationToken ct = default) =>
        db.Set<SafirBroadcastImage>().Where(i => i.BroadcastId == jobId).ExecuteDeleteAsync(ct);

    public async Task LogAsync(string jobId, string level, string message, string? phone = null, CancellationToken ct = default)
    {
        db.Add(new SafirBroadcastLog
        {
            BroadcastId = jobId,
            At = DateTime.Now,
            Level = level,
            Phone = phone,
            Message = message
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private async Task DeleteJobsAsync(System.Linq.Expressions.Expression<Func<SafirBroadcast, bool>> filter, CancellationToken ct)
    {
        var ids = db.Set<SafirBroadcast>().Where(filter).Select(j => j.Id);
        await db.Set<SafirBroadcastLog>().Where(l => ids.Contains(l.BroadcastId)).ExecuteDeleteAsync(ct);
        await db.Set<SafirBroadcastImage>().Where(i => ids.Contains(i.BroadcastId)).ExecuteDeleteAsync(ct);
        await db.Set<SafirBroadcastRecipient>().Where(r => ids.Contains(r.BroadcastId)).ExecuteDeleteAsync(ct);
        await db.Set<SafirBroadcast>().Where(filter).ExecuteDeleteAsync(ct);
    }

    private async Task<SafirBroadcastStatusDto> BuildDtoAsync(SafirBroadcast job, CancellationToken ct)
    {
        var counts = await db.Set<SafirBroadcastRecipient>().AsNoTracking()
            .Where(r => r.BroadcastId == job.Id)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(SafirRecipientStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        var pending = Count(SafirRecipientStatus.Pending);
        var sent = Count(SafirRecipientStatus.Sent);
        var retryable = Count(SafirRecipientStatus.FailedRetryable);
        var permanent = Count(SafirRecipientStatus.FailedPermanent);
        var failed = retryable + permanent;
        var remaining = pending + retryable;
        var active = ActiveStates.Contains(job.State);

        var errors = await db.Set<SafirBroadcastRecipient>().AsNoTracking()
            .Where(r => r.BroadcastId == job.Id && r.Error != null &&
                        (r.Status == SafirRecipientStatus.FailedRetryable || r.Status == SafirRecipientStatus.FailedPermanent))
            .OrderByDescending(r => r.ProcessedAt)
            .Take(MaxRecentErrors)
            .Select(r => r.Phone + ": " + r.Error)
            .ToListAsync(ct);
        errors.Reverse();

        var imageCount = await db.Set<SafirBroadcastImage>().CountAsync(i => i.BroadcastId == job.Id, ct);

        return new SafirBroadcastStatusDto
        {
            JobId = job.Id,
            State = job.State,
            TotalRecipients = pending + sent + failed,
            ProcessedRecipients = sent + failed,
            SuccessCount = sent,
            FailedCount = failed,
            RemainingRecipients = remaining,
            CanResume = !active && remaining > 0,
            ImageCount = imageCount,
            HasText = !string.IsNullOrWhiteSpace(job.Text),
            TextPreview = BuildPreview(job.Text),
            CreatedAt = job.CreatedAt,
            FinishedAt = job.FinishedAt,
            LastActivityAt = job.LastActivityAt,
            Message = job.Message,
            RecentErrors = errors
        };
    }

    private static string OneLine(string value) =>
        value.Replace("\r", " ").Replace("\n", " ").Trim();

    private static string? BuildPreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var oneLine = OneLine(text);
        return oneLine.Length <= MaxTextPreview ? oneLine : oneLine[..MaxTextPreview] + "…";
    }
}
