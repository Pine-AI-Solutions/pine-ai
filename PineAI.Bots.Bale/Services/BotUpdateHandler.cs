using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PineAI.Bots.Bale.Models;
using PineAI.Bots.Shared.Messages;
using PineAI.Bots.Shared.Services;
using PineAI.Bots.Shared.Tools;
using PineAI.Core.Entities;
using PineAI.Persistence.Services;
using PineAI.Shared;

namespace PineAI.Bots.Bale.Services;

/// <summary>
/// Handles incoming Bale bot updates by acting as a transparent bypass between
/// the user and an AI chat agent powered by the Microsoft Agents SDK.
///
/// Flow:
///  1. <c>/start</c> — clears any existing session, greets the user via AI.
///  2. Any other message — forwarded to the AI agent using the user's persisted session.
///  3. If the AI response contains <c>&lt;&lt;ORDER_CODE … &gt;&gt;</c> blocks, the order
///     code is looked up in the database and the result is appended to the reply.
///  4. If the AI response contains <c>&lt;&lt;FEEDBACK … &gt;&gt;</c> blocks, the feedback
///     is routed to the appropriate chat ID based on the feedback type (10 different types supported).
/// </summary>
public class BotUpdateHandler(BaleBotClient botClient,
        PineAIDbContext dbContext,
        IChatAgentService agentService,
        ChatSessionStore sessionStore,
        BotChatMessageQueue chatMessageQueue,
        PhotoMessageStore photoMessageStore,
        UserPenaltyStore penaltyStore,
        ILogger<BotUpdateHandler> logger,
        IConfiguration configuration) : IBotUpdateHandler
{
    /// <summary>Platform name used in user-facing support messages.</summary>
    private const string PlatformName = "بله";

    private readonly List<long> chatIds = new()
    {
        // Ananas Support Groups
        6318588996,5715522360,6215427121,6137308408,
        5518881690,5000226193,5225037607,6178785306,
        5477856928,5172013155,5249048339,
        //Akhlaghi Group
        5372010785,5535142626,6188981039,4413431598,
        5988414706,6020396255,6282035661,
        6309128770,6108224018,5437659346,4427614753,
        5286810467,5135010906
    };

    public async Task HandleAsync(BaleUpdate update, CancellationToken ct)
    {
        var message = update.Message;
        if (message is null)
            return;

        bool hasPhoto = message.Photo is { Length: > 0 };
        bool hasText = !string.IsNullOrWhiteSpace(message.Text);
        bool hasCaption = !string.IsNullOrWhiteSpace(message.Caption);

        // Ignore messages with no text, caption, or photo content
        if (!hasText && !hasPhoto && !hasCaption)
            return;

        var chatId = message.Chat.Id;

        if (chatIds.Contains(message.Chat.Id))
        {
            return;
        }

        var username = message.From?.Username;

        if (string.IsNullOrEmpty(username))
        {
            await botClient.SendMessageAsync(chatId, $"""
                همراه عزیز {configuration["Business:NameFa"]}
                نام کاربری (آیدی) بله شما در دسترس نیست
                جهت امکان پذیر شدن ارتباط با شما
                لطفا نام کاربری (آیدی) خود را ست کنید
                یا اگر ست کرده اید، در دسترسی عمومی قرار دهید
                """, ct);

            return;
        }

        // Silently drop messages from explicitly blocked usernames.
        // No reply is sent, nothing is persisted, and the AI is never called.
        var blockedUsernames = configuration.GetSection("BlockedUsernames").Get<string[]>() ?? [];
        if (blockedUsernames.Contains(username, StringComparer.OrdinalIgnoreCase))
        {
            logger.LogInformation("Blocked username @{Username} — message suppressed", username);
            return;
        }

        logger.LogInformation("Update {UpdateId}: chat={ChatId} hasPhoto={HasPhoto}", update.UpdateId, chatId, hasPhoto);

        // When the user sends a photo, store it for later forwarding
        if (hasPhoto)
        {
            // Use the highest-resolution variant (last element in the array)
            var bestPhoto = message.Photo![^1];
            photoMessageStore.StorePhoto(chatId, message.MessageId);
            logger.LogInformation("Stored photo message_id={MessageId} for chat={ChatId} (file_id={FileId})",
                message.MessageId, chatId, bestPhoto.FileId);
        }

        // A photo-only message (no text, no caption) is stored above and nothing else.
        // We must NOT forward it to the AI — the user may be sending several photos in a row
        // and each would independently trigger an AI response, causing TakePhotos() to drain
        // the store prematurely so later photos are never forwarded to support.
        if (hasPhoto && !hasText && !hasCaption)
            return;

        // Build the text that will be forwarded to the AI.
        // For a photo+caption message the photo is already stored above, so we pass only
        // the raw caption — no wrapper prefix — to avoid confusing the AI into thinking
        // it needs to handle a fresh photo separately from the ones already queued.
        string text;
        if (hasText)
            text = message.Text!.Trim();
        else
            text = message.Caption!.Trim();

        // If the user has queued photos that the AI does not yet know about, append a
        // system note so the AI sets HasPhoto:true in the FEEDBACK block.
        var pendingPhotoCount = photoMessageStore.PeekPhotos(chatId).Count;
        if (pendingPhotoCount > 0)
            text += $"\n[سیستم: کاربر {pendingPhotoCount} تصویر ارسال کرده است]";

        // Enqueue user message for background persistence (fire-and-forget)
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, chatId, text, IsFromBot: false, DateTime.UtcNow));

        // Penalty gate: if the user is locked out, reject without calling the AI.
        // The gate fires AFTER persistence so penalised messages are still auditable.
        // It also fires BEFORE the /start session-reset so users cannot escape the
        // lock by sending /start.
        if (penaltyStore.IsUnderPenalty(chatId))
        {
            logger.LogInformation("Chat {ChatId} is under penalty — message suppressed", chatId);
            await botClient.SendMessageAsync(chatId, BotSharedMessages.PenaltyLocked, ct);
            return;
        }

        // /start → reset session so user always gets a fresh greeting
        if (text.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            sessionStore.RemoveSession(chatId);
        }

        var existingSession = sessionStore.GetSession(chatId);
        var response = await agentService.SendWithSessionAsync(existingSession, text);

        // Strip <<PENALTY>> FIRST and BEFORE saving the session.
        // If the raw block is saved into session history the AI sees it on the next
        // turn, copies it verbatim, and it leaks to the user even after stripping.
        var textAfterPenalty = ResponseBlockTools.StripPenaltyBlocks(response.ResponseText, out var penaltyText);

        if (!string.IsNullOrEmpty(penaltyText))
        {
            // Clear session so the user starts fresh after the lock expires.
            sessionStore.RemoveSession(chatId);
            penaltyStore.ApplyPenalty(chatId);
            logger.LogWarning("Penalty applied to chat {ChatId}. Reason: {Reason}", chatId, penaltyText);
            await SendAndEnqueueBotReplyAsync(chatId, username, BotSharedMessages.PenaltyApplied, ct);
            return;
        }

        // Session is saved only after confirming no penalty block is present.
        sessionStore.SetSession(chatId, response.SerializedSession);

        // Continue normal block processing on the penalty-stripped text
        var orderCodes = new List<string>();
        var visibleAfterOrders = ResponseBlockTools.StripOrderCodeBlocks(textAfterPenalty, orderCodes);
        visibleAfterOrders = ResponseBlockTools.StripFeedbackBlocks(visibleAfterOrders, out var feedbackJson);

        // Strip any <<VERIFICATION>> block the AI emitted. The block carries the AI's
        // proposed "data was sent to support" sentence. We never forward that sentence
        // to the user — each successful HandleXxxAsync method sends its own authoritative
        // confirmation. Discarding the AI's verification here is what guarantees the user
        // is never told their data was delivered when, in fact, no admin dispatch occurred
        // (malformed JSON, missing required fields, missing TargetChatId, …).
        visibleAfterOrders = ResponseBlockTools.StripVerificationBlocks(visibleAfterOrders, out var aiVerificationText);
        FeedbackValidator.ValidateAiVerificationText(aiVerificationText, logger);

        // Strip <<PRODUCT_QUERY>> blocks and resolve them from the DB
        var productQueries = new List<string>();
        var visibleText = ResponseBlockTools.StripProductQueryBlocks(visibleAfterOrders, productQueries);

        if (productQueries.Count > 0)
        {
            var productLines = new List<string>();
            foreach (var query in productQueries)
            {
                var products = await dbContext.Product
                    .AsNoTracking()
                    .Where(p =>
                        EF.Functions.Like(p.ProductName, $"%{query}%") ||
                        EF.Functions.Like(p.Category, $"%{query}%") ||
                        EF.Functions.Like(p.Brand ?? "", $"%{query}%") ||
                        EF.Functions.Like(p.Color ?? "", $"%{query}%") ||
                        EF.Functions.Like(p.Size ?? "", $"%{query}%") ||
                        EF.Functions.Like(p.ProductCode ?? "", $"%{query}%") ||
                        EF.Functions.Like(p.FabricType ?? "", $"%{query}%"))
                    .OrderBy(p => p.Category)
                    .ToListAsync(ct);

                productLines.Add(products.Count > 0
                    ? FormatProductResults(query, products)
                    : $"🔍 محصولی برای «{query}» یافت نشد.");
            }

            if (productQueries.Count > 1)
            {
                // Comparison request: feed all product data back to the AI so it can
                // write a proper comparative analysis in Persian.
                var dataBlock = string.Join("\n\n", productLines);
                var comparisonPrompt =
                    $"[داده سیستم - نتایج جستجوی محصولات برای مقایسه]\n\n{dataBlock}\n\n" +
                    "بر اساس داده‌های بالا، مقایسه جامع و مفیدی از این محصولات برای کاربر بنویس. " +
                    "شباهت‌ها و تفاوت‌های کلیدی (قیمت، برند، رنگ، سایز، جنس پارچه، موجودی) را به‌صورت واضح بیان کن.";

                var currentSession = sessionStore.GetSession(chatId);
                var comparisonResponse = await agentService.SendWithSessionAsync(currentSession, comparisonPrompt);

                // Strip all command blocks from the comparison response — the AI should
                // only produce readable comparison text here, not trigger further actions.
                var comparisonText = ResponseBlockTools.StripPenaltyBlocks(comparisonResponse.ResponseText, out _);
                comparisonText = ResponseBlockTools.StripOrderCodeBlocks(comparisonText);
                comparisonText = ResponseBlockTools.StripFeedbackBlocks(comparisonText, out _);
                comparisonText = ResponseBlockTools.StripVerificationBlocks(comparisonText, out _);
                comparisonText = ResponseBlockTools.StripProductQueryBlocks(comparisonText);

                // Persist the updated session from the second AI call.
                sessionStore.SetSession(chatId, comparisonResponse.SerializedSession);

                visibleText = string.IsNullOrWhiteSpace(comparisonText) ? dataBlock : comparisonText;
            }
            else
            {
                // Single-product lookup: just append the formatted product details.
                var productBlock = productLines[0];
                visibleText = string.IsNullOrWhiteSpace(visibleText)
                    ? productBlock
                    : visibleText + "\n\n" + productBlock;
            }
        }

        // If the AI signalled one or more order codes, resolve them from the DB
        if (orderCodes.Count > 0)
        {
            var statusLines = new List<string>();
            foreach (var orderCode in orderCodes)
            {
                var order = await dbContext.CustomerOrder
                    .Include(o => o.OrderStatus)
                    .FirstOrDefaultAsync(o => o.OrderCode == orderCode, ct);

                if (order != null)
                {
                    statusLines.Add(
                        $"📦 سفارش «{order.OrderCode}»:\n" +
                        $"وضعیت: {order.OrderStatus.Title}\n" +
                        (!string.IsNullOrEmpty(order.PostalTrackingCode) ? $"کد مرسوله پستی: {order.PostalTrackingCode}\n" : "") +
                        $" کد ۲۴ رقمیو بزن تو سایت پست https://tracking.post.ir/ از وضعیت بسته باخبر شو");
                }
                else
                {
                    statusLines.Add($"❌ سفارشی با کد «{orderCode}» یافت نشد.");
                }
            }

            var statusBlock = string.Join("\n\n", statusLines);

            // Append status info after the AI's visible text
            if (!string.IsNullOrWhiteSpace(visibleText))
                visibleText = visibleText + "\n\n" + statusBlock;
            else
                visibleText = statusBlock;

            if (!string.IsNullOrWhiteSpace(visibleText))
                await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);

            // A FEEDBACK block may accompany the ORDER_CODE block (e.g. DelayedDelivery
            // where the AI checks the order and escalates in the same turn). Process it too.
            // If dispatch fails here the order-status reply was already sent, so no fallback needed.
            if (!string.IsNullOrEmpty(feedbackJson))
                await TryDispatchFeedbackAsync(feedbackJson, visibleText: null, chatId, username, ct);
        }
        else if (!string.IsNullOrEmpty(feedbackJson))
        {
            // If the FEEDBACK is invalid/incomplete, fall back to sending the AI's visible text
            // (which should contain the AI asking the user for the missing field). The AI's
            // proposed delivery confirmation has already been removed by StripVerificationBlocks
            // above, so no false "data sent to support" claim can leak through this path.
            await TryDispatchFeedbackAsync(feedbackJson, visibleText: visibleText, chatId, username, ct);
        }
        else
        {
            // No FEEDBACK block at all — the AI is just chatting with the user.
            if (!string.IsNullOrWhiteSpace(visibleText))
                await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);
        }
    }

    /// <summary>Sends a bot reply to the user and enqueues it for background persistence.</summary>
    private async Task SendAndEnqueueBotReplyAsync(long userChatId, string username, string text, CancellationToken ct)
    {
        await botClient.SendMessageAsync(userChatId, text, ct);
        chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, text, IsFromBot: true, DateTime.UtcNow));
    }

    /// <summary>
    /// Parses <paramref name="feedbackJson"/>, validates that all required fields for the
    /// feedback type are present and non-placeholder, then dispatches to the admin-group
    /// handler. Returns <c>true</c> when dispatching succeeded.
    ///
    /// When any check fails the method:
    /// <list type="bullet">
    ///   <item>logs a structured warning,</item>
    ///   <item>sends <paramref name="visibleText"/> to the user (when not null/empty) so the
    ///         AI's follow-up question — asking for the missing field — reaches the user, and</item>
    ///   <item>returns <c>false</c> without contacting any admin group.</item>
    /// </list>
    /// </summary>
    private async Task<bool> TryDispatchFeedbackAsync(
        string feedbackJson, string? visibleText, long chatId, string username, CancellationToken ct)
    {
        // The caller has already stripped any <<VERIFICATION>> block from visibleText,
        // so the only sentences that can survive in this fallback path are the AI's
        // ask/answer text (e.g. "please send your order code"). No further sanitisation
        // of free-form prose is required: false delivery confirmations can only enter
        // the visible stream through a VERIFICATION block, and those are gone.

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(feedbackJson);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Feedback JSON produced by AI is malformed — skipping admin notification");
            if (!string.IsNullOrWhiteSpace(visibleText))
                await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (!root.TryGetProperty("Type", out var typeProp))
            {
                logger.LogWarning("Feedback JSON missing 'Type' field — skipping admin notification");
                if (!string.IsNullOrWhiteSpace(visibleText))
                    await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);
                return false;
            }

            var feedbackType = typeProp.GetString() ?? string.Empty;

            if (!FeedbackValidator.ValidateFeedbackJson(feedbackType, root, logger))
            {
                // ValidateFeedbackJson already logged per-field warnings.
                // Send the AI's visible text — it should contain the question asking for the missing field.
                if (!string.IsNullOrWhiteSpace(visibleText))
                    await SendAndEnqueueBotReplyAsync(chatId, username, visibleText, ct);
                return false;
            }

            await HandleFeedbackAsync(chatId, root, username, ct);
            return true;
        }
    }

    private async Task HandleFeedbackAsync(long userChatId, JsonElement root, string username, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(username))
        {
            var noUsernameMsg = $"دوست عزیز {configuration["Business:NameFa"]}، لطفاً نام کاربری خود را در بله تنظیم کنید و در دسترس قرار دهید تا بتوانیم به شما پاسخ دهیم.";
            await botClient.SendMessageAsync(userChatId, noUsernameMsg, ct);
            chatMessageQueue.TryEnqueue(new BotChatMessageEntry(userChatId.ToString(), userChatId, noUsernameMsg, IsFromBot: true, DateTime.UtcNow));
            return;
        }

        // Type was already validated upstream; read it again for routing.
        var feedbackType = root.TryGetProperty("Type", out var typeProp)
            ? typeProp.GetString() ?? string.Empty
            : string.Empty;

        // Read target chat ID directly from the JSON generated by the AI
        if (!root.TryGetProperty("TargetChatId", out var chatIdProperty) ||
            !chatIdProperty.TryGetInt64(out long targetChatId))
        {
            logger.LogWarning("Feedback JSON missing or invalid 'TargetChatId' for type: {FeedbackType}", feedbackType);
            return;
        }

        // Skip routing if chat ID is not configured (0 means placeholder)
        if (targetChatId == 0)
        {
            logger.LogWarning("Chat ID not configured for feedback type: {FeedbackType}", feedbackType);
            var unconfiguredMsg = BotSharedMessages.SupportAcknowledgement(PlatformName);
            await botClient.SendMessageAsync(userChatId, unconfiguredMsg, ct);
            chatMessageQueue.TryEnqueue(new BotChatMessageEntry(username, userChatId, unconfiguredMsg, IsFromBot: true, DateTime.UtcNow));
            return;
        }

        string userBaleUsername = $"\n کاربری: @{username}";

        // Route to appropriate handler based on feedback type
        switch (feedbackType)
        {
            case "Satisfaction":
                await HandleSatisfactionAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "Complaint":
                await HandleComplaintAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "DefectiveProduct":
                await HandleDefectiveProductAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "PhotoMismatch":
                await HandlePhotoMismatchAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "ReturnedPackage":
                await HandleReturnedPackageAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "Wholesale":
                await HandleWholesaleAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "NoOrderCode":
                await HandleNoOrderCodeAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "FailedPayment":
                await HandleFailedPaymentAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "DelayedDelivery":
                await HandleDelayedDeliveryAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "WrongSize":
                await HandleWrongSizeAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "UnknownQuery":
                await HandleUnknownQueryAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "InStoreBillingError":
                await HandleInStoreBillingErrorAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "InStoreComplaint":
                await HandleInStoreComplaintAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            case "StoreHoursQuery":
                await HandleStoreHoursQueryAsync(userChatId, targetChatId, root, userBaleUsername, username, ct);
                break;

            default:
                logger.LogWarning("Unhandled feedback type: {FeedbackType}", feedbackType);
                break;
        }
    }

    private async Task HandleSatisfactionAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SatisfactionSuccess, ct);
        var log = FeedbackLogBuilder.BuildSatisfactionLog(root, userBaleUsername);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleComplaintAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.ComplaintSuccess(PlatformName), ct);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupComplaintOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildComplaintLog(root, userBaleUsername, orderInfo);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleDefectiveProductAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        bool hasPhoto = FeedbackValidator.GetHasPhoto(root);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildDefectiveProductLog(root, userBaleUsername, hasPhoto, orderInfo);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
        await ForwardStoredPhotosAsync(userChatId, targetChatId, hasPhoto, "DefectiveProduct", ct);
    }

    private async Task HandlePhotoMismatchAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        bool hasPhoto = FeedbackValidator.GetHasPhoto(root);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildPhotoMismatchLog(root, userBaleUsername, hasPhoto, orderInfo);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
        await ForwardStoredPhotosAsync(userChatId, targetChatId, hasPhoto, "PhotoMismatch", ct);
    }

    private async Task HandleReturnedPackageAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildReturnedPackageLog(root, userBaleUsername, orderInfo);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleWholesaleAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.WholesaleSuccess(PlatformName), ct);
        var log = FeedbackLogBuilder.BuildWholesaleLog(root, userBaleUsername);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleNoOrderCodeAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.ReviewSuccess(PlatformName), ct);
        var log = FeedbackLogBuilder.BuildNoOrderCodeLog(root, userBaleUsername);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleFailedPaymentAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.ReviewSuccess(PlatformName), ct);
        var log = FeedbackLogBuilder.BuildFailedPaymentLog(root, userBaleUsername);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleDelayedDeliveryAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.FollowUpSuccess(PlatformName), ct);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildDelayedDeliveryLog(root, userBaleUsername, orderInfo);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleWrongSizeAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        var orderCode = root.TryGetProperty("OrderCode", out var ocProp) ? ocProp.GetString() : null;
        var orderInfo = await FeedbackLogBuilder.LookupOrderAsync(orderCode, dbContext);
        var log = FeedbackLogBuilder.BuildWrongSizeLog(root, userBaleUsername, orderInfo);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleUnknownQueryAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.MessageReceivedSuccess(PlatformName), ct);
        var log = FeedbackLogBuilder.BuildUnknownQueryLog(root, userBaleUsername);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleInStoreBillingErrorAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.SupportAcknowledgement(PlatformName), ct);
        bool hasPhoto = FeedbackValidator.GetHasPhoto(root);
        var log = FeedbackLogBuilder.BuildInStoreBillingErrorLog(root, userBaleUsername, hasPhoto);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
        await ForwardStoredPhotosAsync(userChatId, targetChatId, hasPhoto, "InStoreBillingError", ct);
    }

    private async Task HandleInStoreComplaintAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.InStoreComplaintSuccess, ct);
        var log = FeedbackLogBuilder.BuildInStoreComplaintLog(root, userBaleUsername);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    private async Task HandleStoreHoursQueryAsync(long userChatId, long targetChatId, JsonElement root, string userBaleUsername, string username, CancellationToken ct)
    {
        await SendAndEnqueueBotReplyAsync(userChatId, username, BotSharedMessages.StoreHoursQuerySuccess, ct);
        var log = FeedbackLogBuilder.BuildStoreHoursQueryLog(root, userBaleUsername);
        await botClient.SendMessageAsync(targetChatId, log, CancellationToken.None);
    }

    /// <summary>
    /// Formats a list of products matching a search query into a user-facing Persian text block.
    /// </summary>
    private static string FormatProductResults(string query, List<Product> products)
    {
        var lines = new List<string>
        {
            $"🛍 نتایج جستجو برای «{query}»:"
        };

        for (var i = 0; i < products.Count; i++)
        {
            var p = products[i];
            var parts = new List<string> { $"\n📌 محصول {i + 1}: {p.ProductName}" };

            if (!string.IsNullOrWhiteSpace(p.ProductCode))
                parts.Add($"کد محصول: {p.ProductCode}");

            parts.Add($"دسته‌بندی: {p.Category}");

            if (!string.IsNullOrWhiteSpace(p.Brand))
                parts.Add($"برند: {p.Brand}");

            if (!string.IsNullOrWhiteSpace(p.Size))
                parts.Add($"سایز: {p.Size}");

            if (!string.IsNullOrWhiteSpace(p.Color))
                parts.Add($"رنگ: {p.Color}");

            if (!string.IsNullOrWhiteSpace(p.FabricType))
                parts.Add($"جنس پارچه: {p.FabricType}");

            parts.Add($"قیمت: {p.Price:N0} تومان");
            parts.Add($"موجودی: {p.AvailableCount} عدد");

            lines.Add(string.Join("\n", parts));
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Forwards stored photos for a user to the target admin chat when the AI indicated
    /// a photo was received.  Logs a warning when no photo is found in the store.
    /// </summary>
    private async Task ForwardStoredPhotosAsync(
        long userChatId, long targetChatId, bool hasPhoto, string feedbackType, CancellationToken ct)
    {
        if (!hasPhoto)
            return;

        var storedMessageIds = photoMessageStore.TakePhotos(userChatId);
        if (storedMessageIds.Count > 0)
        {
            foreach (var msgId in storedMessageIds)
                await botClient.ForwardMessageAsync(targetChatId, userChatId, msgId, CancellationToken.None);
        }
        else
        {
            logger.LogWarning(
                "HasPhoto=true for {FeedbackType} but no stored photo found for chat {ChatId}",
                feedbackType, userChatId);
        }
    }
}
