using PineAI.Core.Entities;
using PineAI.Core.Features.Safir;

namespace PineAI.Core.Contracts;

/// <summary>
/// Database-backed Safir broadcast jobs. The API creates / resumes / cancels jobs and reads their status;
/// PineAI.SafirSender claims queued jobs and records the progress. Both sides only talk through the database.
/// </summary>
public interface ISafirBroadcastService
{
    // ── used by the API ────────────────────────────────────────────────────

    Task<bool> HasActiveJobAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves a new job in state Queued; recipients are copied set-based from the distinct customer phone numbers.
    /// TotalRecipients is 0 (and nothing is saved) when there is nobody to send to.
    /// </summary>
    Task<(string JobId, int TotalRecipients)> CreateAsync(string? text, IReadOnlyList<SafirImageData> images,
        string createdByUserId, CancellationToken ct = default);

    Task<SafirBroadcastStatusDto?> GetStatusAsync(string jobId, CancellationToken ct = default);
    Task<SafirBroadcastStatusDto?> GetLatestStatusAsync(CancellationToken ct = default);
    Task<List<SafirBroadcastStatusDto>> GetResumableAsync(CancellationToken ct = default);
    Task<List<SafirBroadcastLogDto>> GetLogsAsync(string jobId, int take, CancellationToken ct = default);

    /// <summary>Queues a stopped job again. Returns null on success, otherwise the reason (Persian).</summary>
    Task<string?> ResumeAsync(string jobId, CancellationToken ct = default);

    /// <summary>Requests a stop. Returns null on success, otherwise the reason (Persian).</summary>
    Task<string?> CancelAsync(string jobId, CancellationToken ct = default);

    /// <summary>Deletes a job and everything stored for it. Returns null on success, otherwise the reason.</summary>
    Task<string?> DeleteAsync(string jobId, CancellationToken ct = default);

    // ── used by PineAI.SafirSender ─────────────────────────────────────────

    /// <summary>Jobs left in Running by a stopped sender become Interrupted; old jobs are deleted.</summary>
    Task RecoverAsync(int retentionDays, CancellationToken ct = default);

    /// <summary>Atomically takes the oldest Queued job (state becomes Running). Null when there is none.</summary>
    Task<SafirBroadcast?> ClaimNextAsync(CancellationToken ct = default);

    Task<List<SafirImageData>> GetImagesAsync(string jobId, CancellationToken ct = default);

    /// <summary>Recipients not handled yet, in a stable order.</summary>
    Task<List<string>> GetPendingPhonesAsync(string jobId, CancellationToken ct = default);

    Task RecordSuccessAsync(string jobId, string phone, CancellationToken ct = default);
    Task RecordFailureAsync(string jobId, string phone, string reason, bool permanent, CancellationToken ct = default);

    /// <summary>Updates the activity time; returns true when the user asked to cancel.</summary>
    Task<bool> HeartbeatAsync(string jobId, CancellationToken ct = default);

    Task FinishAsync(string jobId, SafirBroadcastJobState state, string? message = null, CancellationToken ct = default);

    /// <summary>True when somebody still needs a message (never attempted or failed with a temporary error).</summary>
    Task<bool> HasRemainingAsync(string jobId, CancellationToken ct = default);

    Task DeleteImagesAsync(string jobId, CancellationToken ct = default);

    Task LogAsync(string jobId, string level, string message, string? phone = null, CancellationToken ct = default);
}
