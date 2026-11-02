namespace PineAI.SafirSender.Services;

/// <summary>Error codes documented by Safir (docs/safir-docs.md).</summary>
public static class SafirErrorCodes
{
    public const int InternalServerError = 2;
    public const int RateLimitExceeded = 3;
    public const int InvalidInput = 4;
    public const int InvalidPhone = 8;
    public const int NotBaleUser = 17;
    public const int PaymentRequired = 20;
    public const int MaximumContactLimitReached = 21;
}

public sealed record SafirUploadResult(bool Success, string? FileId, string? Error)
{
    public static SafirUploadResult Ok(string fileId) => new(true, fileId, null);
    public static SafirUploadResult Fail(string error) => new(false, null, error);
}

public sealed record SafirSendResult(bool Success, string? MessageId, int? ErrorCode, string? Error, int? HttpStatus)
{
    public static SafirSendResult Ok(string? messageId) => new(true, messageId, null, null, 200);
    public static SafirSendResult Fail(string error, int? errorCode = null, int? httpStatus = null) =>
        new(false, null, errorCode, error, httpStatus);

    public bool IsRateLimited => ErrorCode == SafirErrorCodes.RateLimitExceeded || HttpStatus == 429;

    /// <summary>
    /// The number itself can never receive the message (bad input, invalid phone, not a Bale user).
    /// Resuming a broadcast does not retry these recipients.
    /// </summary>
    public bool IsPermanentForRecipient =>
        ErrorCode is SafirErrorCodes.InvalidInput or SafirErrorCodes.InvalidPhone or SafirErrorCodes.NotBaleUser;

    /// <summary>Errors after which every further message would fail too, so a broadcast should stop.</summary>
    public bool IsFatal =>
        ErrorCode == SafirErrorCodes.PaymentRequired || HttpStatus is 401 or 403;
}
