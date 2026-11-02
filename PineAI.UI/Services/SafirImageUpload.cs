namespace PineAI.UI.Services;

/// <summary>An image chosen on the Safir broadcast page, ready to be posted to the API.</summary>
public sealed record SafirImageUpload(string FileName, string ContentType, byte[] Content);
