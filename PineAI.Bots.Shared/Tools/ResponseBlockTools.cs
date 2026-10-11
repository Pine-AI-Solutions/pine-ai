namespace PineAI.Bots.Shared.Tools;

/// <summary>
/// Utility for parsing structured command blocks embedded in AI agent responses.
/// The AI can embed blocks like:
/// <code>
/// &lt;&lt;ORDER_CODE
/// ORD-12345
/// &gt;&gt;
/// </code>
/// This class strips those blocks from the visible response text and optionally
/// collects the extracted values so the application can act on them.
/// </summary>
public static class ResponseBlockTools
{
    private const string OrderCodeStart = "<<ORDER_CODE";
    private const string FeedbackStart = "<<FEEDBACK";
    private const string PenaltyStart = "<<PENALTY";
    private const string VerificationStart = "<<VERIFICATION";
    private const string ProductQueryStart = "<<PRODUCT_QUERY";
    private const string BlockEnd = ">>";

    /// <summary>
    /// Strips all <c>&lt;&lt;ORDER_CODE … &gt;&gt;</c> blocks from <paramref name="text"/>.
    /// Extracted order codes are appended to <paramref name="collectedOrderCodes"/> when provided.
    /// </summary>
    /// <param name="text">Raw AI response text that may contain ORDER_CODE blocks.</param>
    /// <param name="collectedOrderCodes">
    /// Optional list to receive the trimmed order code value from each block.
    /// </param>
    /// <returns>The cleaned response text with all ORDER_CODE blocks removed.</returns>
    public static string StripOrderCodeBlocks(string text, List<string>? collectedOrderCodes = null)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var startIndex = 0;

        while (startIndex < text.Length)
        {
            var blockStart = text.IndexOf(OrderCodeStart, startIndex, StringComparison.OrdinalIgnoreCase);
            if (blockStart == -1)
                break;

            var blockEnd = text.IndexOf(BlockEnd, blockStart + OrderCodeStart.Length, StringComparison.OrdinalIgnoreCase);
            if (blockEnd == -1)
                break;

            var content = text
                .Substring(blockStart + OrderCodeStart.Length, blockEnd - (blockStart + OrderCodeStart.Length))
                .Trim();

            if (collectedOrderCodes is not null && content.Length > 0)
                collectedOrderCodes.Add(NormalizeDigits(content));

            var blockLength = (blockEnd + BlockEnd.Length) - blockStart;
            text = text.Remove(blockStart, blockLength);
            startIndex = blockStart;
        }

        return text.Trim();
    }

    /// <summary>
    /// Strips all <c>&lt;&lt;PRODUCT_QUERY … &gt;&gt;</c> blocks from <paramref name="text"/>.
    /// Extracted search queries are appended to <paramref name="collectedQueries"/> when provided.
    /// </summary>
    /// <param name="text">Raw AI response text that may contain PRODUCT_QUERY blocks.</param>
    /// <param name="collectedQueries">
    /// Optional list to receive the trimmed query string from each block.
    /// </param>
    /// <returns>The cleaned response text with all PRODUCT_QUERY blocks removed.</returns>
    public static string StripProductQueryBlocks(string text, List<string>? collectedQueries = null)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var startIndex = 0;

        while (startIndex < text.Length)
        {
            var blockStart = text.IndexOf(ProductQueryStart, startIndex, StringComparison.OrdinalIgnoreCase);
            if (blockStart == -1)
                break;

            var blockEnd = text.IndexOf(BlockEnd, blockStart + ProductQueryStart.Length, StringComparison.OrdinalIgnoreCase);
            if (blockEnd == -1)
                break;

            var content = text
                .Substring(blockStart + ProductQueryStart.Length, blockEnd - (blockStart + ProductQueryStart.Length))
                .Trim();

            if (collectedQueries is not null && content.Length > 0)
                collectedQueries.Add(content);

            var blockLength = (blockEnd + BlockEnd.Length) - blockStart;
            text = text.Remove(blockStart, blockLength);
            startIndex = blockStart;
        }

        return text.Trim();
    }

    /// <summary>
    /// Strips all <c>&lt;&lt;FEEDBACK … &gt;&gt;</c> blocks from <paramref name="text"/>.
    /// Extracted feedback JSON is returned via <paramref name="collectedFeedbackData"/>.
    /// </summary>
    /// <param name="text">Raw AI response text that may contain FEEDBACK blocks.</param>
    /// <param name="collectedFeedbackData">
    /// Output parameter to receive the trimmed feedback JSON from the block.
    /// </param>
    /// <returns>The cleaned response text with all FEEDBACK blocks removed.</returns>
    public static string StripFeedbackBlocks(string text, out string? collectedFeedbackData)
    {
        collectedFeedbackData = null;

        if (string.IsNullOrEmpty(text))
            return text;

        var startIndex = 0;

        while (startIndex < text.Length)
        {
            var blockStart = text.IndexOf(FeedbackStart, startIndex, StringComparison.OrdinalIgnoreCase);
            if (blockStart == -1)
                break;

            var blockEnd = text.IndexOf(BlockEnd, blockStart + FeedbackStart.Length, StringComparison.OrdinalIgnoreCase);
            if (blockEnd == -1)
                break;

            var content = text
                .Substring(blockStart + FeedbackStart.Length, blockEnd - (blockStart + FeedbackStart.Length))
                .Trim();

            if (content.Length > 0)
                collectedFeedbackData = content;

            var blockLength = (blockEnd + BlockEnd.Length) - blockStart;
            text = text.Remove(blockStart, blockLength);
            startIndex = blockStart;
        }

        return text.Trim();
    }

    /// <summary>
    /// Strips all <c>&lt;&lt;VERIFICATION … &gt;&gt;</c> blocks from <paramref name="text"/>.
    /// The trimmed inner text of the (last) block is returned via
    /// <paramref name="verificationText"/>, or <c>null</c> when no block is present.
    /// </summary>
    /// <remarks>
    /// A VERIFICATION block carries the confirmation sentence the AI proposes after
    /// it emits a <c>&lt;&lt;FEEDBACK&gt;&gt;</c> block (e.g. "your message was sent
    /// to support"). The handler — not the AI — decides whether such a confirmation
    /// actually reaches the user, so the block is always stripped from visible text
    /// and only its inner text is exposed to the caller for inspection / logging.
    /// </remarks>
    /// <param name="text">Raw AI response text that may contain VERIFICATION blocks.</param>
    /// <param name="verificationText">
    /// Output parameter to receive the trimmed inner text from the block, or <c>null</c>
    /// if no block was found.
    /// </param>
    /// <returns>The cleaned response text with all VERIFICATION blocks removed.</returns>
    public static string StripVerificationBlocks(string text, out string? verificationText)
    {
        verificationText = null;

        if (string.IsNullOrEmpty(text))
            return text;

        var startIndex = 0;

        while (startIndex < text.Length)
        {
            var blockStart = text.IndexOf(VerificationStart, startIndex, StringComparison.OrdinalIgnoreCase);
            if (blockStart == -1)
                break;

            var blockEnd = text.IndexOf(BlockEnd, blockStart + VerificationStart.Length, StringComparison.OrdinalIgnoreCase);
            if (blockEnd == -1)
                break;

            var content = text
                .Substring(blockStart + VerificationStart.Length, blockEnd - (blockStart + VerificationStart.Length))
                .Trim();

            if (content.Length > 0)
                verificationText = content;

            var blockLength = (blockEnd + BlockEnd.Length) - blockStart;
            text = text.Remove(blockStart, blockLength);
            startIndex = blockStart;
        }

        return text.Trim();
    }

    /// <summary>
    /// Strips the <c>&lt;&lt;PENALTY … &gt;&gt;</c> block from <paramref name="text"/>.
    /// Extracted penalty text is returned via <paramref name="penaltyText"/>.
    /// This method is intentionally lenient about how the AI terminates the block:
    /// <list type="bullet">
    ///   <item>Accepts the standard <c>&gt;&gt;</c> terminator.</item>
    ///   <item>Accepts <c>&gt; &gt;</c> with whitespace between the angle brackets.</item>
    ///   <item>Accepts the Persian/Arabic right-pointing double angle quotation mark <c>»</c> (U+00BB).</item>
    ///   <item>Accepts the HTML-encoded form <c>&amp;gt;&amp;gt;</c>.</item>
    ///   <item>If no terminator is found, treats end-of-text as the implicit close so
    ///         a malformed block is still stripped and its text is best-effort extracted.</item>
    /// </list>
    /// Also handles the case where the AI wraps the block in a markdown code fence
    /// (e.g. <c>```text … ```</c>), which is stripped before searching for the block.
    /// </summary>
    /// <param name="text">Raw AI response text that may contain a PENALTY block.</param>
    /// <param name="penaltyText">
    /// Output parameter to receive the trimmed plain-text reason from the block.
    /// </param>
    /// <returns>The cleaned response text with the PENALTY block removed.</returns>
    public static string StripPenaltyBlocks(string text, out string? penaltyText)
    {
        penaltyText = null;

        if (string.IsNullOrEmpty(text))
            return text;

        // Normalize alternate terminator forms to the canonical ">>" so the search
        // below uses a single literal. This handles AI variations such as Persian
        // double angle quotation marks, HTML-encoded angle brackets, and ">" pairs
        // separated by whitespace.
        text = NormalizePenaltyTerminators(text);

        // The AI sometimes wraps the <<PENALTY>> block in a markdown code fence
        // (```text ... ``` or ``` ... ```) because the instruction file shows it
        // as a formatted example. Strip any such fence that contains <<PENALTY
        // before doing the normal block search.
        text = StripCodeFencesContaining(text, PenaltyStart);

        var startIndex = 0;

        while (startIndex < text.Length)
        {
            var blockStart = text.IndexOf(PenaltyStart, startIndex, StringComparison.OrdinalIgnoreCase);
            if (blockStart == -1)
                break;

            var contentStart = blockStart + PenaltyStart.Length;
            var blockEnd = text.IndexOf(BlockEnd, contentStart, StringComparison.OrdinalIgnoreCase);

            int contentEnd;
            int blockLength;
            if (blockEnd == -1)
            {
                // No closing ">>" found — the AI forgot to terminate the block.
                // Treat the rest of the text as the block body and strip it entirely
                // so the raw block can never leak to the user.
                contentEnd = text.Length;
                blockLength = text.Length - blockStart;
            }
            else
            {
                contentEnd = blockEnd;
                blockLength = (blockEnd + BlockEnd.Length) - blockStart;
            }

            var content = text.Substring(contentStart, contentEnd - contentStart).Trim();

            if (content.Length > 0)
                penaltyText = content;

            text = text.Remove(blockStart, blockLength);
            startIndex = blockStart;
        }

        return text.Trim();
    }

    /// <summary>
    /// Replaces non-standard ">>" terminator variants with the canonical "&gt;&gt;"
    /// so block stripping can rely on a single literal search.
    /// </summary>
    private static string NormalizePenaltyTerminators(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // HTML-encoded angle brackets
        if (text.Contains("&gt;&gt;", StringComparison.OrdinalIgnoreCase))
            text = text.Replace("&gt;&gt;", BlockEnd, StringComparison.OrdinalIgnoreCase);

        // Persian/Arabic right-pointing double angle quotation mark (U+00BB)
        if (text.Contains('\u00BB'))
            text = text.Replace("\u00BB", BlockEnd);

        // "> >" with any amount of whitespace between the two angle brackets,
        // but only when preceded by a newline so we don't accidentally rewrite
        // a comparison operator embedded in normal Persian text.
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(?<=\n)\s*>\s+>",
            BlockEnd);

        return text;
    }

    /// <summary>
    /// Removes markdown code fences (``` ... ```) from <paramref name="text"/>
    /// when the fenced content contains <paramref name="marker"/>.
    /// Only the fence delimiters are removed; the inner content is preserved so
    /// subsequent block-stripping logic can find the embedded command.
    /// </summary>
    private static string StripCodeFencesContaining(string text, string marker)
    {
        const string fence = "```";
        var startIndex = 0;

        while (startIndex < text.Length)
        {
            var fenceStart = text.IndexOf(fence, startIndex, StringComparison.Ordinal);
            if (fenceStart == -1)
                break;

            // Find the closing fence (must be after the opening one)
            var fenceEnd = text.IndexOf(fence, fenceStart + fence.Length, StringComparison.Ordinal);
            if (fenceEnd == -1)
                break;

            var fenceCloseEnd = fenceEnd + fence.Length;
            var fencedContent = text.Substring(fenceStart, fenceCloseEnd - fenceStart);

            if (fencedContent.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Extract just the inner content (skip the opening line which may be ```text)
                var innerStart = text.IndexOf('\n', fenceStart);
                if (innerStart == -1 || innerStart >= fenceEnd)
                {
                    // No newline found — remove the whole fence block
                    text = text.Remove(fenceStart, fenceCloseEnd - fenceStart);
                    startIndex = fenceStart;
                }
                else
                {
                    var inner = text.Substring(innerStart + 1, fenceEnd - (innerStart + 1)).TrimEnd();
                    text = text.Remove(fenceStart, fenceCloseEnd - fenceStart).Insert(fenceStart, inner);
                    startIndex = fenceStart;
                }
            }
            else
            {
                startIndex = fenceCloseEnd;
            }
        }

        return text;
    }

    /// <summary>
    /// Converts Persian (
    /// </summary>
    public static string NormalizeDigits(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            // Persian-Indic: U+06F0–U+06F9
            if (chars[i] >= '\u06F0' && chars[i] <= '\u06F9')
                chars[i] = (char)(chars[i] - '\u06F0' + '0');
            // Arabic-Indic: U+0660–U+0669
            else if (chars[i] >= '\u0660' && chars[i] <= '\u0669')
                chars[i] = (char)(chars[i] - '\u0660' + '0');
        }
        return new string(chars);
    }
}
