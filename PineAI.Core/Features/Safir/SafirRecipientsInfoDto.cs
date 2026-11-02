namespace PineAI.Core.Features.Safir;

public class SafirRecipientsInfoDto
{
    /// <summary>Number of distinct customer phone numbers a broadcast would be sent to.</summary>
    public int RecipientCount { get; set; }

    /// <summary>False when the Safir access key / bot id are missing in the API configuration.</summary>
    public bool IsConfigured { get; set; }
}
