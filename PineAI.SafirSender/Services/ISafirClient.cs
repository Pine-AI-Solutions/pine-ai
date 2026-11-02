namespace PineAI.SafirSender.Services;

/// <summary>Thin wrapper over the Bale Safir REST API (https://safir.bale.ai/api/v3).</summary>
public interface ISafirClient
{
    /// <summary>True when the access key and bot id are configured.</summary>
    bool IsConfigured { get; }

    /// <summary>POST /api/v3/upload_file. Returns the file_id to use in <see cref="SendMessageAsync"/>.</summary>
    Task<SafirUploadResult> UploadFileAsync(byte[] content, string fileName, string contentType, CancellationToken ct);

    /// <summary>
    /// POST /api/v3/send_message. Sends a text message, an image (file_id) with optional caption, or both.
    /// <paramref name="phoneNumber"/> is the 10 digit number stored on the customer (without 98 / 0).
    /// <paramref name="requestId"/> makes the call idempotent on Safir's side.
    /// </summary>
    Task<SafirSendResult> SendMessageAsync(string phoneNumber, string? text, string? fileId, string requestId, CancellationToken ct);
}
