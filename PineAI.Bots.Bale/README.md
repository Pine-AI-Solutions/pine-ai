# PineAI.Bots.Bale

A .NET 10 Worker Service that runs as a Bale Messenger chatbot for **Ananas Collection Boutique** (مزون اناناس کالکشن).  
It acts as a smart customer-support agent powered by an AI language model, processes customer messages, resolves order lookups from the database, and routes escalation requests to the correct human-support Bale chats.

> **Note:** This project was previously named `PineAI.BaleBot`. It has been renamed to `PineAI.Bots.Bale` as part of a multi-bot refactoring that introduced the shared core `PineAI.Bots.Shared` project and the new `PineAI.Bots.Telegram` project.

---

## Project Structure

```
PineAI.Bots.Shared/          ← Shared core: AI services, session/photo/penalty stores,
│                                response block tools, persistence workers
│
PineAI.Bots.Bale/            ← This project: Bale-specific models, client, worker, handler
│   Models/BaleModels.cs
│   Services/BaleBotClient.cs
│   Services/BotUpdateHandler.cs
│   Services/IBotUpdateHandler.cs
│   Workers/BaleBotWorker.cs
│   Chat/                    ← AI instruction Markdown files
│   Program.cs
│
PineAI.Bots.Telegram/        ← Telegram-specific models, client, worker, handler
```

---

## Table of Contents

1. [What This Project Does](#what-this-project-does)
2. [Architecture Overview](#architecture-overview)
3. [Message Handling Flow](#message-handling-flow)
4. [AI Agent & Instructions](#ai-agent--instructions)
5. [Feedback Types (Escalation Routing)](#feedback-types-escalation-routing)
6. [Photo Handling](#photo-handling)
7. [Services Reference](#services-reference)
8. [Workers Reference](#workers-reference)
9. [Models Reference](#models-reference)
10. [Configuration](#configuration)
11. [Running the Project](#running-the-project)
12. [Adding or Changing Bot Instructions](#adding-or-changing-bot-instructions)

---

## What This Project Does

`PineAI.Bots.Bale` continuously polls the [Bale Bot API](https://dev.bale.ai) (`tapi.bale.ai`) for incoming messages using long-polling (`getUpdates`).  
For every message it receives from a customer, it:

1. **Passes the message to an AI agent** that has been pre-loaded with a Persian-language customer-support instruction document (`Chat/chtbot-instructions-main.md`).
2. **Parses structured command blocks** from the AI's response:
   - `<<ORDER_CODE … >>` — triggers a live database lookup and appends the order status and postal tracking code to the reply.
   - `<<FEEDBACK … >>` — triggers routing of a structured notification to one of 11 predefined human-support Bale chat IDs based on the feedback type.
   - `<<PRODUCT_QUERY … >>` — triggers a live database search for products matching the search term; results include product name, code, category, brand, size, color, fabric type, price, and stock. When two or more blocks are present (comparison request), the system feeds all results back to the AI for a comparative analysis.
3. **Sends the final reply back to the customer** via `sendMessage`.
4. **Forwards photos** to the appropriate support chat when the AI flags `HasPhoto: true` (e.g. for defective-product reports).
5. **Persists all conversations** (both customer messages and bot replies) to the database via a fire-and-forget background queue.

All AI, session management, photo/penalty stores, and persistence logic lives in **`PineAI.Bots.Shared`** and is shared with the Telegram bot.

---

## Architecture Overview

```
Bale API  ──────────────────────────────────────────────────────────────────────
  │                                                                             │
  │  getUpdates (long-poll, 30 s)                sendMessage / forwardMessage  │
  ▼                                                                             │
BaleBotWorker (BackgroundService)                          [PineAI.Bots.Bale]  │
  │                                                                             │
  │  dispatch each update                                                       │
  ▼                                                                             │
BotUpdateHandler (IBotUpdateHandler, Scoped)               [PineAI.Bots.Bale]  │
  │                                                                             │
  ├── PhotoMessageStore  (store photo message IDs for later forwarding)        │  [Shared]
  │                                                                             │
  ├── IChatAgentService  (send text to AI, get back raw response)              │  [Shared]
  │       ├── ChatAgentService       (GitHub Models / OpenAI-compatible)       │
  │       └── ArvanChatAgentService  (ArvanCloud OAI-compatible)               │
  │                                                                             │
  ├── ResponseBlockTools  (parse <<ORDER_CODE>>, <<FEEDBACK>>, <<PRODUCT_QUERY>> blocks)       │  [Shared]
  │                                                                             │
  ├── PineAIDbContext    (EF Core – order lookups)                             │
  │                                                                             │
  ├── BotChatMessageQueue (Channel<T> – fire-and-forget persistence)           │  [Shared]
  │       └── BotChatMessageSaverWorker (BackgroundService – drains queue)     │
  │                                                                             │
  └── BaleBotClient  (HTTP wrapper for Bale Bot API)  ─────────────────────► │
```

---

## Message Handling Flow

```
Incoming update
      │
      ├─ message is null?                          → ignore
      ├─ message has no text, caption, or photo?   → ignore
      ├─ chat ID is in internal support list?      → ignore (prevent loops)
      ├─ user has no Bale username?                → ask user to set one
      │
      ├─ message has a photo?
      │     └─ store (chatId → messageId) in PhotoMessageStore
      │
      ├─ user is under active penalty? (UserPenaltyStore)
      │     └─ send locked message → return (AI never called, /start escape blocked)
      │
      ├─ build AI input:
      │     text message     → pass as-is
      │     photo + caption  → "[کاربر یک تصویر با توضیح ...]\n<caption>"
      │     photo only       → "[کاربر یک تصویر ارسال کرد]"
      │
      ├─ /start command?
      │     └─ remove session from ChatSessionStore (fresh greeting)
      │
      ├─ send text to IChatAgentService.SendWithSessionAsync()
      │     └─ uses/creates per-user session (conversation history)
      │
      ├─ parse AI response:
      │     StripPenaltyBlocks()    → if <<PENALTY>> found → apply 10-min lock → return
      │     StripOrderCodeBlocks()  → collect order codes
      │     StripFeedbackBlocks()   → collect feedback JSON
      │     StripVerificationBlocks() → strip AI delivery confirmation text
      │     StripProductQueryBlocks() → collect product search terms
      │
      ├─ PRODUCT_QUERY present?
      │     ├─ single query  → look up in DB, append formatted product card to reply
      │     └─ multiple queries (comparison) → look up all in DB, feed results back
      │             to AI for a second call → AI writes a Persian comparative analysis
      │
      ├─ ORDER_CODE present?
      │     └─ look up each order in DB, append status + postal tracking code
      │
      └─ FEEDBACK present?
            └─ validate required fields against RequiredFeedbackFields map
                  ├─ if missing fields → send visible fallback text to user asking for them
                  └─ if valid → route to the correct handler (see Feedback Types below)
                        └─ if HasPhoto: true → ForwardMessageAsync() stored photos
```

---

## AI Agent & Instructions

The AI system prompt is loaded at startup from every `*.md` file inside the `Chat/` directory.  
Currently there are two main files defining distinct conversational profiles:
- **`Chat/chtbot-instructions-main.md`**: Ananas Collection Boutique bot profile.
- **`Chat/chtbot-instructions-akhlaghi.md`**: Akhlaghi Dress bot profile (includes in-store workflows).

These files define:
- The bot's persona (a polite Persian-language support assistant).
- All in-scope and out-of-scope topics.
- Exact response scripts for each workflow.
- The JSON templates and required fields for `<<ORDER_CODE>>` and `<<FEEDBACK>>` blocks.
- The `<<PRODUCT_QUERY>>` block rules: when to emit it, how to form the search term, and how to handle the data returned by the system (including product name, code, category, brand, size, color, fabric type, price, and stock).
- The comparison flow: when two or more `<<PRODUCT_QUERY>>` blocks are emitted, the system feeds all product data back to the AI for a second turn so it can write a comparative analysis.
- A knowledge base (products, shipping, policies, etc.) specific to each branch.

**To modify the bot's behavior, edit the relevant `chtbot-instructions-*.md` file.**  
The files are automatically copied to the output directory at build time (`CopyToOutputDirectory: Always`).
And remember: Never generate `<<FEEDBACK>>` until every required field has been explicitly provided.

### AI Provider Switch

Set `AiProvider` in `appsettings.json`:

| Value | Implementation | Backend |
|---|---|---|
| `github` *(default)* | `ChatAgentService` | GitHub Models / any OpenAI-compatible endpoint |
| `arvan` | `ArvanChatAgentService` | ArvanCloud OAI-compatible REST API |

Both implementations are in `PineAI.Bots.Shared` and share the same `IChatAgentService` interface.

---

## Feedback Types (Escalation Routing)

When the AI cannot resolve an issue itself, it emits a `<<FEEDBACK … >>` block containing a JSON object with a `Type` field. `BotUpdateHandler` validates that **all required fields** (defined in `RequiredFeedbackFields`) exist. If valid, it routes the notification to the target human-support group:

| Type | Description | Target Chat |
|---|---|---|
| `Satisfaction` | Customer appreciation / positive review | 6318588996 |
| `Complaint` | Unresolved complaint after KB attempt | 5715522360 |
| `DefectiveProduct` | Torn / stained / broken item (+ optional photo forward) | 6215427121 |
| `PhotoMismatch` | Product doesn't match website photo | 6137308408 |
| `ReturnedPackage` | Package returned / delivered to sender | 5518881690 |
| `Wholesale` | Wholesale order inquiry (6+ pieces) | 5000226193 |
| `NoOrderCode` | Customer lost their order code | 5225037607 |
| `UnknownQuery` | Anything outside the knowledge base | 6178785306 |
| `FailedPayment` | Payment deducted but order not confirmed | 5477856928 |
| `DelayedDelivery` | Order not arrived after 8+ business days | 5172013155 |
| `WrongSize` | Size doesn't fit | 5249048339 |
| `InStoreBillingError` | Akhlaghi branch billing dispute | *See instruction configuration* |
| `InStoreComplaint` | Akhlaghi branch staff complaint | *See instruction configuration* |

---

## Photo Handling

When a customer sends a photo (e.g. to show a defective product):

1. `BotUpdateHandler` detects `message.Photo != null` and calls `PhotoMessageStore.StorePhoto(chatId, messageId)`.
2. The message text sent to the AI is synthesised as a Persian placeholder so the AI knows a photo was attached.
3. When the AI generates a `DefectiveProduct` FEEDBACK block with `"HasPhoto": true`, the handler:
   - Calls `PhotoMessageStore.TakePhotos(chatId)` to retrieve stored message IDs.
   - Calls `BaleBotClient.ForwardMessageAsync(targetChatId, userChatId, messageId)` for each photo.
4. `PhotoMessageStoreCleanupWorker` (in `PineAI.Bots.Shared`) runs every 5 minutes and evicts any photo entries older than 5 minutes.

---

## Services Reference

| Class | Lifetime | Responsibility | Location |
|---|---|---|---|
| `BaleBotClient` | Singleton | HTTP wrapper for `sendMessage`, `forwardMessage`, `getUpdates` | PineAI.Bots.Bale |
| `ChatSessionStore` | Singleton | In-memory map of `chatId → serialized session JSON` | PineAI.Bots.Shared |
| `PhotoMessageStore` | Singleton | In-memory map of `chatId → [messageId, …]` for pending photo forwards (TTL: 5 min) | PineAI.Bots.Shared |
| `UserPenaltyStore` | Singleton | In-memory map of `chatId → PenaltyEntry(ExpiresAt)` for active 10-minute locks | PineAI.Bots.Shared |
| `BotChatMessageQueue` | Singleton | Bounded channel for fire-and-forget message persistence | PineAI.Bots.Shared |
| `IChatAgentService` | Singleton | AI provider abstraction (`ChatAgentService` or `ArvanChatAgentService`) | PineAI.Bots.Shared |
| `BotUpdateHandler` | Scoped | Core message dispatch logic (one instance per update) | PineAI.Bots.Bale |

---

## Workers Reference

| Class | Type | Responsibility | Location |
|---|---|---|---|
| `BaleBotWorker` | `BackgroundService` | Long-polls `getUpdates` (30 s timeout), dispatches each update concurrently using dual-semaphore concurrency | PineAI.Bots.Bale |
| `BotChatMessageSaverWorker` | `BackgroundService` | Drains `BotChatMessageQueue` and persists each entry to the `BotChatMessage` table | PineAI.Bots.Shared |
| `PhotoMessageStoreCleanupWorker` | `BackgroundService` | Periodically evicts expired photo entries from `PhotoMessageStore` | PineAI.Bots.Shared |
| `PenaltyStoreCleanupWorker` | `BackgroundService` | Evicts expired penalty entries from `UserPenaltyStore` every 10 minutes | PineAI.Bots.Shared |

---

## Penalty System

When the AI detects that a user has crossed red lines 10 or more times in their current session, or detects spam (the same message sent ≥ 3 times in a row), it emits a `<<PENALTY … >>` block in its response. The block body is plain text (a brief Persian reason).

### How it works

1. `ResponseBlockTools.StripPenaltyBlocks()` extracts the block from the AI response before any other processing.
2. `BotUpdateHandler` calls `UserPenaltyStore.ApplyPenalty(chatId)`, which records `ExpiresAt = UtcNow + 10 minutes`.
3. A Persian notification is sent to the user: `⛔ به دلیل رفتار نامناسب مکرر، دسترسی شما به مدت ۱۰ دقیقه محدود شد.`
4. For the next 10 minutes, **every message** from that `chatId` (including `/start`) is intercepted by the penalty gate before the AI is ever called.
5. All blocked messages are still persisted to the database for audit via `BotChatMessageQueue`.

---

## Concurrent Processing Model

`BaleBotWorker` processes updates from the same `getUpdates` batch concurrently using a **dual-semaphore** strategy.

| Semaphore | Capacity | Purpose |
|---|---|---|
| `globalSemaphore` | 10 | Caps total in-flight handlers across all users. |
| `perUserSemaphores[chatId]` | 1 | Serialises all messages from the **same** user, preserving AI session ordering. |

---

## Models Reference

| Class | Description |
|---|---|
| `BaleUpdate` | A single update from `getUpdates` |
| `BaleMessage` | A Bale message (text, caption, photo array, sender, chat) |
| `BaleUser` | Sender info (id, first name, last name, username) |
| `BaleChat` | Chat info (id, type) |
| `PhotoSize` | One resolution variant of a photo |
| `BaleApiResponse<T>` | Generic wrapper for all Bale API responses |
| `BaleSendMessageRequest` | Request body for `sendMessage` |
| `BaleForwardMessageRequest` | Request body for `forwardMessage` |

---

## Configuration

All settings live in `appsettings.json` (and user secrets for sensitive values).

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection": "<SQL Server or SQLite connection string>"
  },
  "DatabaseProvider": "SqlServer",   // or "Sqlite"

  "BaleMessenger": {
    "BaseUrl": "https://tapi.bale.ai/",
    "Token": "<your Bale bot token>"
  },

  "AiProvider": "github",            // or "arvan"

  // Used when AiProvider = "github"
  "AiAgent": {
    "ApiKey": "<GitHub Models / OpenAI API key>",
    "Model": "gpt-4.1",
    "Endpoint": "https://models.github.ai/inference"
  },

  // Used when AiProvider = "arvan"
  "ArvanAiAgent": {
    "ApiKey": "<ArvanCloud API key>",
    "Model": "GPT-OSS-120B",
    "Endpoint": "https://arvancloudai.ir/gateway/models/GPT-OSS-120B/<KEY>/v1"
  },

  "Seq": {
    "ServerUrl": "http://localhost:5341"   // structured log sink (optional)
  }
}
```

---

## Running the Project

```bash
# Build only the Bale bot and its dependencies
dotnet build PineAI.Bots.Bale/PineAI.Bots.Bale.csproj

# Run in development (SQLite mode – no SQL Server needed)
cd PineAI.Bots.Bale
dotnet run
```

The service can also be installed as a **Windows Service** via `sc.exe` or the .NET publish + `--service` pattern (`Microsoft.Extensions.Hosting.WindowsServices` is already configured).

---

## Adding or Changing Bot Instructions

1. Edit (or add a new `*.md` file to) `PineAI.Bots.Bale/Chat/`.
2. Rebuild — new files are auto-copied to the output directory.
3. Restart the service; `InitAsync()` reloads all `*.md` files on startup.

> **Tip for future agents:** The instruction file `chtbot-instructions-main.md` is the single source of truth for the bot's Persian-language behavior, knowledge base, and all FEEDBACK routing rules. Any behavioral change should start there, then verify that `BotUpdateHandler` handles the corresponding FEEDBACK `Type` (add a new `case` in the `switch` and a new private `HandleXxxAsync` method if needed). The same instruction files are mirrored in `PineAI.Bots.Telegram/Chat/` for the Telegram bot.
