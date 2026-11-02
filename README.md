# PineAI

> An AI-powered customer support platform for online shops — adaptable to any business.

## فارسی

**PineAI** یک پلتفرم پشتیبانی هوشمند مشتریان بر پایه .NET 10 است که برای هر فروشگاه اینترنتی قابل تنظیم و استقرار می‌باشد. این پلتفرم از چت‌بات‌های هوشمند (بله و تلگرام)، وب‌اپلیکیشن‌های مدیریتی، و سرویس‌های پشتیبان برای رسیدگی به پیام‌های مشتریان، استعلام وضعیت سفارش‌ها و ارجاع موارد به تیم پشتیبانی انسانی تشکیل شده است.

### قابلیت‌های اصلی

- پردازش پیام‌های متنی، کپشن و تصاویر از طریق **بله** و **تلگرام**
- پاسخ‌گویی خودکار به مشتریان با استفاده از مدل‌های هوش مصنوعی
- استعلام وضعیت سفارش و کد رهگیری مستقیم از پایگاه داده
- فوروارد خودکار عکس‌ها به تیم پشتیبانی (مثلاً برای گزارش کالای معیوب)
- ارجاع خودکار موارد پیچیده به چت‌های پشتیبانی انسانی از پیش تعریف‌شده
- بهبود خودکار دستورالعمل‌های ربات با تحلیل مکالمات واقعی
- پشتیبانی از **SQL Server** و **SQLite**
- رابط کاربری مدیریتی برای نظارت بر مکالمات و تنظیمات

### معماری سیستم (پروژه‌های solution)

| پروژه | نوع | نقش |
|---|---|---|
| `PineAI.Core` | Class Library | قراردادها (اینترفیس‌ها)، موجودیت‌ها و DTOها |
| `PineAI.Persistence` | Class Library | EF Core، ریپازیتوری‌ها، کشینگ و صف پیام‌ها |
| `PineAI.Identity` | Class Library | احراز هویت با ASP.NET Core Identity |
| `PineAI.Shared` | Class Library | ابزارهای مشترک (تقویم شمسی، XLSX builder) |
| `PineAI.Bots.Shared` | Class Library | هسته مشترک چت‌بات‌ها (سرویس AI، مدیریت session، صف پیام) |
| `PineAI.Bots.Bale` | Worker Service | چت‌بات بله |
| `PineAI.Bots.Telegram` | Worker Service | چت‌بات تلگرام |
| `PineAI.Api` | ASP.NET Core Web API | REST API اصلی، اعلان سفارش، پشتیبانی از SMS |
| `PineAI.Api.Landing` | ASP.NET Core Web API | API مربوط به صفحه معرفی |
| `PineAI.UI` | Blazor Web App | پنل مدیریتی |
| `PineAI.Landing` | Blazor Web App | صفحه معرفی محصول |
| `PineAI.OrderTrack` | Blazor Web App | پیگیری سفارش توسط مشتری |
| `PineAI.InstructionAnalyzer` | Console App | بهبود خودکار دستورالعمل‌های ربات |
| `PineAI.Backup` | Worker Service | پشتیبان‌گیری خودکار از پایگاه داده |

### مدل پردازش همزمان

`BaleBotWorker` و `TelegramBotWorker` از طراحی **dual-semaphore** استفاده می‌کنند:

- یک semaphore سراسری برای محدود کردن تعداد کل آپدیت‌های همزمان
- یک semaphore مجزا برای هر کاربر جهت حفظ ترتیب پیام‌های همان کاربر

این طراحی باعث می‌شود پیام‌های کاربران مختلف همزمان پردازش شوند، اما پیام‌های یک کاربر هرگز با هم قاطی نشوند.

---

## English

**PineAI** is a **.NET 10** AI-powered customer support platform for online shops. It is fully adaptable to any e-commerce business and provides chatbots (Bale and Telegram), admin web apps, and background services that handle customer messages, resolve order lookups, and escalate cases to human support when needed.

### Core Features

- Customer support via **Bale Messenger** and **Telegram** chatbots
- AI-assisted automated responses using configurable language models
- Live order status and postal tracking lookup from the database
- Automatic photo forwarding to support teams (e.g. defective product reports)
- Configurable escalation routing to predefined human-support chats
- Automatic instruction improvement via LLM analysis of real conversations (`PineAI.InstructionAnalyzer`)
- Admin panel for monitoring conversations and managing settings
- Customer-facing order-tracking web app
- Supports both **SQL Server** and **SQLite**

### Solution Structure

```
PineAI.slnx
│
├── Application
│   ├── PineAI.Core           ← Domain contracts (interfaces), entities, DTOs
│   ├── PineAI.Persistence    ← EF Core DbContext, repositories, caching, message queue
│   └── PineAI.Identity       ← ASP.NET Core Identity (separate IdentityContext)
│
├── Shared
│   ├── PineAI.Shared         ← Cross-cutting utilities (Persian calendar, XLSX builder)
│   └── PineAI.Bots.Shared    ← Shared bot core: AI services, session/photo/penalty stores,
│                                response block tools, persistence workers
│
└── Presentation
    ├── Api
    │   ├── PineAI.Api            ← Main REST API (order notifications, SMS, webhooks)
    │   └── PineAI.Api.Landing    ← Landing page API
    │
    ├── Services
    │   ├── PineAI.Bots.Bale      ← Bale Messenger chatbot worker
    │   ├── PineAI.Bots.Telegram  ← Telegram chatbot worker
    │   └── PineAI.Backup         ← Automated database backup worker
    │
    ├── Web
    │   ├── PineAI.UI             ← Admin panel (Blazor)
    │   ├── PineAI.Landing        ← Product landing page (Blazor)
    │   └── PineAI.OrderTrack     ← Customer order-tracking app (Blazor)
    │
    └── Consoles
        └── PineAI.InstructionAnalyzer  ← LLM-based bot instruction optimizer
```

### Bot Processing Pipeline (`PineAI.Bots.Bale` / `PineAI.Bots.Telegram`)

1. Bot worker long-polls the messaging API and dispatches updates with a **dual-semaphore** model.
2. `BotUpdateHandler` orchestrates each update: penalty check → AI call → structured block parsing → order lookup / escalation → persistence.
3. `IChatAgentService` abstracts two AI backends, selected at startup via `AiProvider` config:
   - `"github"` → `ChatAgentService` (GitHub Models / any OpenAI-compatible endpoint)
   - `"arvan"` → `ArvanChatAgentService` (ArvanCloud OpenAI-compatible API)
4. Both services load system-prompt instructions by concatenating all `*.md` files from the `Chat/` folder at startup.

### AI Structured Response Blocks

The AI embeds command blocks in its replies that are stripped before the text reaches the user:

| Block | Purpose |
|---|---|
| `<<ORDER_CODE … >>` | Triggers a live order-status lookup in the database |
| `<<FEEDBACK … >>` | Routes the conversation to a predefined human support chat |
| `<<PENALTY … >>` | Applies a 10-minute lockout to the user |
| `<<VERIFICATION … >>` | Carries a confirmation sentence; always stripped, never shown |

### Data Layer

- `PineAIDbContext` (in `PineAI.Persistence`) holds all domain entities.
- `PineAIIdentityContext` (in `PineAI.Identity`) is a separate context for ASP.NET Identity.
- SQL Server uses EF migrations; SQLite uses `EnsureCreated()`.
- Caching uses **FusionCache** with an optional Redis backplane.

### Build

```bash
dotnet build PineAI.slnx
```

### Technologies

| Technology | Version | Usage |
|---|---|---|
| .NET | 10.0 | Main runtime |
| ASP.NET Core | 10.0 | REST API and web apps |
| Blazor | 10.0 | Admin panel, landing page, order tracking |
| Worker Service | 10.0 | Background bot and backup services |
| Entity Framework Core | 10.0 | Data access (SQL Server & SQLite) |
| FusionCache | latest | In-memory + Redis distributed caching |
| Microsoft.Extensions.AI | 10.3.0 | AI integration abstraction |
| OpenAI SDK | 2.8.0 | AI provider client |
| Microsoft.Agents.AI | 1.0.0-preview | Agent-oriented AI support |
| Serilog | 10.x | Structured logging |
| Seq | 9.0.0 sink | Centralized log sink |

### Configuration Keys

| Key | Values | Effect |
|---|---|---|
| `DatabaseProvider` | `SqlServer` (default), `Sqlite` | Switches EF provider |
| `AiProvider` | `github` (default), `arvan` | Selects AI backend |
| `AiAgent:*` | ApiKey, Model, Endpoint | GitHub Models / OpenAI config |
| `ArvanAiAgent:*` | ApiKey, Model, Endpoint | ArvanCloud config |
| `BaleMessenger:Token` | Bot token | Bale API authentication |
| `BaleMessenger:Safir` / `BaleMessenger:BotId` | Safir Api Access Key / sender bot id | Bale Safir bulk messaging (`PineAI.Api`, UI page `/safir/broadcast`) |
| `BaleMessenger:SafirBaseUrl` | URL (default `https://safir.bale.ai`) | Safir service address |
| `BaleMessenger:BroadcastDelayMs` | milliseconds (default `150`) | Pause between two Safir messages of a broadcast |
| `BaleMessenger:BroadcastStoragePath` | folder (default `App_Data/safir-broadcasts`) | Where unfinished broadcasts (message, images, recipients, progress) are kept so they survive a restart and can be resumed; the API process needs write access |
| `BaleMessenger:BroadcastRetentionDays` | days (default `30`) | Saved broadcasts older than this are deleted on startup |
| `Telegram:Token` | Bot token | Telegram API authentication |
| `ConnectionStrings:Redis` | Connection string | Enables Redis backplane for caching |
| `Seq:ServerUrl` | URL | Centralized Serilog log sink (optional) |

### Customizing Bot Behavior

Bot instructions are plain Markdown files loaded from the `Chat/` folder inside each bot project. To adapt the bot to your shop:

1. Edit (or replace) the `*.md` files in `PineAI.Bots.Bale/Chat/` and `PineAI.Bots.Telegram/Chat/`.
2. Define your shop's persona, policies, product knowledge, and escalation rules.
3. Configure the feedback routing targets (support group/chat IDs) in `appsettings.json`.
4. Run `PineAI.InstructionAnalyzer` periodically to let the LLM suggest improvements based on real customer conversations.

The instruction files are automatically copied to the output directory at build time and reloaded on every startup.
