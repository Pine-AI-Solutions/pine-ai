using PineAI.Core.Features.Safir;

namespace PineAI.Core.Entities;

/// <summary>
/// One "send this message to all customers" run. Created by the API, executed by PineAI.SafirSender.
/// Everything needed to continue it later lives in the database (message, images, recipients and who already got it).
/// </summary>
public class SafirBroadcast
{
    [Key]
    [StringLength(32)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? Text { get; set; }

    [StringLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public SafirBroadcastJobState State { get; set; } = SafirBroadcastJobState.Queued;

    /// <summary>Job-level message (for example why the job failed or was stopped).</summary>
    public string? Message { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>Last time the sender worked on this job. Lets the UI see that a running job is alive.</summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>Set by the API; the sender stops the job as soon as it sees it.</summary>
    public bool CancelRequested { get; set; }

    public List<SafirBroadcastImage> Images { get; set; } = new();
    public List<SafirBroadcastRecipient> Recipients { get; set; } = new();
    public List<SafirBroadcastLog> Logs { get; set; } = new();
}

public class SafirBroadcastImage
{
    [Key]
    public int Id { get; set; }

    [StringLength(32)]
    public string BroadcastId { get; set; } = string.Empty;
    public SafirBroadcast? Broadcast { get; set; }

    public int Position { get; set; }

    [StringLength(260)]
    public string FileName { get; set; } = string.Empty;

    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public class SafirBroadcastRecipient
{
    [Key]
    public int Id { get; set; }

    [StringLength(32)]
    public string BroadcastId { get; set; } = string.Empty;
    public SafirBroadcast? Broadcast { get; set; }

    [StringLength(32)]
    public string Phone { get; set; } = string.Empty;

    public SafirRecipientStatus Status { get; set; } = SafirRecipientStatus.Pending;

    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

/// <summary>Task log of a broadcast, kept in the database.</summary>
public class SafirBroadcastLog
{
    [Key]
    public long Id { get; set; }

    [StringLength(32)]
    public string BroadcastId { get; set; } = string.Empty;
    public SafirBroadcast? Broadcast { get; set; }

    public DateTime At { get; set; } = DateTime.Now;

    [StringLength(16)]
    public string Level { get; set; } = "Information";

    [StringLength(32)]
    public string? Phone { get; set; }

    public string Message { get; set; } = string.Empty;
}
