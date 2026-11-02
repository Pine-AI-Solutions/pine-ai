namespace PineAI.Core.Features.Safir;

/// <summary>Progress snapshot of a Safir broadcast job (message sent to all distinct customer phone numbers).</summary>
public class SafirBroadcastStatusDto
{
    public string JobId { get; set; } = string.Empty;
    public SafirBroadcastJobState State { get; set; }
    public int TotalRecipients { get; set; }

    /// <summary>Recipients handled so far (succeeded + failed).</summary>
    public int ProcessedRecipients { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }

    /// <summary>
    /// Recipients that were not delivered yet and are still worth trying: never attempted, or failed
    /// with a temporary error. Recipients that are permanently undeliverable (e.g. not a Bale user) are not counted.
    /// </summary>
    public int RemainingRecipients { get; set; }

    /// <summary>True when the job is not running and <see cref="RemainingRecipients"/> &gt; 0.</summary>
    public bool CanResume { get; set; }

    public int ImageCount { get; set; }
    public bool HasText { get; set; }

    /// <summary>First characters of the message, so unfinished jobs can be recognized in a list.</summary>
    public string? TextPreview { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>Last time the sender worked on the job (shows that a running job is alive).</summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>Job-level
    public string? Message { get; set; }

    /// <summary>Sample of the most recent per-recipient errors (capped).</summary>
    public List<string> RecentErrors { get; set; } = new();

    public bool IsActive => State is SafirBroadcastJobState.Queued or SafirBroadcastJobState.Running;
}
