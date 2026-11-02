namespace PineAI.SafirSender.Services;

/// <summary>
/// Bound to the existing "BaleMessenger" configuration section.
/// Safir = Api Access Key, BotId = numeric id of the sender bot.
/// </summary>
public class SafirSettings
{
    public const string SectionName = "BaleMessenger";

    public const string DefaultBaseUrl = "https://safir.bale.ai";

    /// <summary>Base url of the Safir service. Falls back to <see cref="DefaultBaseUrl"/> when empty.</summary>
    public string? SafirBaseUrl { get; set; }

    /// <summary>The organization's Api Access Key (sent in the "api-access-key" header).</summary>
    public string? Safir { get; set; }

    /// <summary>Numeric id of the bot that sends the messages (kept as string so an empty value in appsettings binds safely).</summary>
    public string? BotId { get; set; }

    /// <summary>Pause between two messages of a broadcast, to stay below Safir's rate limit.</summary>
    public int BroadcastDelayMs { get; set; } = 150;

    /// <summary>Before the first number and before every following N-th number, the message is also sent to tester customers. 0 disables it.</summary>
    public int TesterBatchSize { get; set; } = 5000;

    /// <summary>Finished
    public int BroadcastRetentionDays { get; set; } = 30;
}
