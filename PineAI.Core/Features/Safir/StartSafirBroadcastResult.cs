namespace PineAI.Core.Features.Safir;

public class StartSafirBroadcastResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? JobId { get; set; }
    public int TotalRecipients { get; set; }
}
