using PineAI.Persistence;
using PineAI.SafirSender;
using PineAI.SafirSender.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, loggerConfig) =>
{
    loggerConfig
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("AppName", "PineAI.SafirSender")
        .WriteTo.Console();

    var seq = builder.Configuration["Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seq))
        loggerConfig.WriteTo.Seq(seq);
});

builder.Services.AddWindowsService(o => o.ServiceName = "PineAI.SafirSender");

builder.Services.AddPersistenceServices(builder.Configuration);

builder.Services.Configure<SafirSettings>(builder.Configuration.GetSection(SafirSettings.SectionName));
builder.Services.AddHttpClient("BaleMessenger", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<ISafirClient, SafirClient>();
builder.Services.AddHostedService<SafirBroadcastWorker>();

var host = builder.Build();
host.Run();
