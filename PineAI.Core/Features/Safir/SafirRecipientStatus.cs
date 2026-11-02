namespace PineAI.Core.Features.Safir;

public enum SafirRecipientStatus
{
    /// <summary>Not attempted yet.</summary>
    Pending = 0,
    Sent = 1,

    /// <summary>Failed with a temporary error; retried when the job is resumed.</summary>
    FailedRetryable = 2,

    /// <summary>The number can never receive the message; never retried.</summary>
    FailedPermanent = 3
}

public sealed record SafirImageData(string FileName, string ContentType, byte[] Content);

public sealed record SafirBroadcastLogDto(DateTime At, string Level, string? Phone, string Message);
