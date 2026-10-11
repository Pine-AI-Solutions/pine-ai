using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using PineAI.Bots.Bale.Services;
using PineAI.Bots.Bale.Workers;
using PineAI.Bots.Shared.Services;
using PineAI.Bots.Shared.Workers;
using PineAI.Persistence;
using PineAI.Persistence.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Required for running as a Windows Service (handles SCM lifecycle, etc.)
    builder.Services.AddWindowsService(options => options.ServiceName = builder.Configuration["Business:Name"]);

    if (!Environment.UserInteractive)
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

    builder.Services.AddSerilog((services, loggerConfig) => loggerConfig
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("AppName", "PineAI.Bots.Bale")
        .WriteTo.Console()
        .WriteTo.Seq(builder.Configuration["Seq:ServerUrl"]));

    var provider = builder.Configuration["DatabaseProvider"] ?? "SqlServer";
    var connStr = builder.Configuration.GetConnectionString("DefaultConnection")!;

    builder.Services.AddDbContext<PineAIDbContext>(options =>
    {
        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            options.UseSqlite(connStr);
        else
            options.UseSqlServer(connStr);
    });

    var token = builder.Configuration["BaleMessenger:Token"] ?? string.Empty;
    builder.Services.AddHttpClient("BaleBotClient", client =>
    {
        client.BaseAddress = new Uri($"https://tapi.bale.ai/bot{token}/");
        client.Timeout = TimeSpan.FromSeconds(60);
    });

    var aiProvider = builder.Configuration["AiProvider"] ?? "github";
    if (aiProvider.Equals("arvan", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddHttpClient("ArvanAiClient");
        builder.Services.AddSingleton<IChatAgentService, ArvanChatAgentService>();
    }
    else
        builder.Services.AddSingleton<IChatAgentService, ChatAgentService>();

    var businessSettings = builder.Configuration.GetSection("Business").Get<BusinessSettings>() ?? new BusinessSettings();
    builder.Services.AddSingleton(businessSettings);

    builder.Services.AddSingleton<BotChatMessageQueue>();
    builder.Services.AddSingleton<ChatSessionStore>();
    builder.Services.AddSingleton<PhotoMessageStore>();
    builder.Services.AddSingleton<UserPenaltyStore>();
    builder.Services.AddSingleton<BaleBotClient>();
    builder.Services.AddScoped<IBotUpdateHandler, BotUpdateHandler>();

    builder.Services.AddHostedService<BaleBotWorker>();
    builder.Services.AddHostedService<BotChatMessageSaverWorker>();
    builder.Services.AddHostedService<PhotoMessageStoreCleanupWorker>();
    builder.Services.AddHostedService<PenaltyStoreCleanupWorker>();

    var host = builder.Build();

    host.Services.InitializeDatabase();

    var agentService = host.Services.GetRequiredService<IChatAgentService>();
    await agentService.InitAsync();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "BaleBot host terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
