using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PineAI.SafirSender.Services;

public sealed class SafirClient : ISafirClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory httpClientFactory;
    private readonly SafirSettings settings;
    private readonly ILogger<SafirClient> logger;
    private readonly long botId;

    public SafirClient(IHttpClientFactory httpClientFactory, IOptions<SafirSettings> options, ILogger<SafirClient> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.settings = options.Value;
        this.logger = logger;
        this.botId = long.TryParse(settings.BotId, out var id) ? id : 0;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.Safir) && botId > 0;

    public async Task<SafirUploadResult> UploadFileAsync(byte[] content, string fileName, string contentType, CancellationToken ct)
    {
        if (!IsConfigured)
            return SafirUploadResult.Fail("تنظیمات سفیر (BaleMessenger:Safir / BotId) انجام نشده است");

        try
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
            form.Add(fileContent, "file", fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("api/v3/upload_file"));
            request.Headers.Add("api-access-key", settings.Safir);
            request.Content = form;

            using var response = await httpClientFactory.CreateClient("BaleMessenger").SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            var parsed = ParseUploadResponse(body);

            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(parsed.FileId))
                return SafirUploadResult.Ok(parsed.FileId!);

            var error = parsed.Error ?? $"HTTP {(int)response.StatusCode}";
            logger.LogWarning("Safir upload_file failed for {FileName}: {Status} {Error}",
                Sanitize(fileName), (int)response.StatusCode, Sanitize(error));
            return SafirUploadResult.Fail(error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception uploading file {FileName} to Safir", Sanitize(fileName));
            return SafirUploadResult.Fail(ex.Message);
        }
    }

    public async Task<SafirSendResult> SendMessageAsync(
        string phoneNumber, string? text, string? fileId, string requestId, CancellationToken ct)
    {
        if (!IsConfigured)
            return SafirSendResult.Fail("تنظیمات سفیر (BaleMessenger:Safir / BotId) انجام نشده است");

        var safePhone = Sanitize(phoneNumber);

        try
        {
            var body = new SendMessageRequest
            {
                RequestId = requestId,
                BotId = botId,
                PhoneNumber = ToSafirPhone(phoneNumber),
                MessageData = new MessageData
                {
                    Message = new MessageBody
                    {
                        Text = string.IsNullOrWhiteSpace(text) ? null : text,
                        FileId = string.IsNullOrWhiteSpace(fileId) ? null : fileId
                    }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("api/v3/send_message"));
            request.Headers.Add("api-access-key", settings.Safir);
            request.Content = JsonContent.Create(body, options: JsonOptions);

            using var response = await httpClientFactory.CreateClient("BaleMessenger").SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            var parsed = ParseSendResponse(responseBody);

            if (response.IsSuccessStatusCode && parsed.ErrorCode is null)
                return SafirSendResult.Ok(parsed.MessageId);

            var error = parsed.Error ?? $"HTTP {(int)response.StatusCode}";
            logger.LogWarning("Safir send_message failed for {Phone}: {Status} code={Code} {Error}",
                safePhone, (int)response.StatusCode, parsed.ErrorCode, Sanitize(error));
            return SafirSendResult.Fail(error, parsed.ErrorCode, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception sending Safir message to {Phone}", safePhone);
            return SafirSendResult.Fail(ex.Message);
        }
    }

    /// <summary>Safir requires 98 + 10 digits, with no 0, spaces or dashes.</summary>
    internal static string ToSafirPhone(string phoneNumber)
    {
        var digits = new string((phoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());

        if (digits.StartsWith("98") && digits.Length == 12) return digits;
        if (digits.StartsWith("0")) digits = digits[1..];
        return "98" + digits;
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(settings.SafirBaseUrl)
            ? SafirSettings.DefaultBaseUrl
            : settings.SafirBaseUrl!;

        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path);
    }

    private static (string? FileId, string? Error) ParseUploadResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? fileId = root.TryGetProperty("file_id", out var f) && f.ValueKind == JsonValueKind.String
                ? f.GetString()
                : null;

            string? error = null;
            if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
                error = DescribeError(e);

            return (fileId, error);
        }
        catch (JsonException)
        {
            return (null, string.IsNullOrWhiteSpace(body) ? null : Truncate(body));
        }
    }

    private static (string? MessageId, int? ErrorCode, string? Error) ParseSendResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            string? messageId = root.TryGetProperty("message_id", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;

            if (root.TryGetProperty("error_data", out var errors) && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                int? code = first.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
                return (messageId, code, DescribeError(first));
            }

            return (messageId, null, null);
        }
        catch (JsonException)
        {
            return (null, null, string.IsNullOrWhiteSpace(body) ? null : Truncate(body));
        }
    }

    private static string DescribeError(JsonElement error)
    {
        var code = error.TryGetProperty("code", out var c) ? c.ToString() : "?";
        var description = error.TryGetProperty("description", out var d) ? d.GetString() : null;
        return string.IsNullOrWhiteSpace(description) ? $"code {code}" : $"{description} (code {code})";
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];

    // Prevents log forging from user-controlled values.
    private static string Sanitize(string? value) =>
        (value ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n");

    private sealed class SendMessageRequest
    {
        [JsonPropertyName("request_id")] public string? RequestId { get; set; }
        [JsonPropertyName("bot_id")] public long BotId { get; set; }
        [JsonPropertyName("phone_number")] public string PhoneNumber { get; set; } = string.Empty;
        [JsonPropertyName("message_data")] public MessageData MessageData { get; set; } = new();
    }

    private sealed class MessageData
    {
        [JsonPropertyName("message")] public MessageBody? Message { get; set; }
    }

    private sealed class MessageBody
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("file_id")] public string? FileId { get; set; }
    }
}
