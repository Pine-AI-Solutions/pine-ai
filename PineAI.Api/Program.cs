using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using PineAI.Api.Auth;
using PineAI.Api.Queue;
using PineAI.Api.Swagger;
using PineAI.Api.Workers;
using PineAI.Core;
using PineAI.Core.Middlewares;
using PineAI.Identity;
using PineAI.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfig) => loggerConfig
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("AppName", "PineAI.Api")
    .WriteTo.Console()
#if DEBUG
    .WriteTo.Debug()
#endif
    .WriteTo.Seq(context.Configuration["Seq:ServerUrl"]));

builder.Services.AddControllers();
builder.Services.AddResponseCompression();
builder.Services.AddCoreServices();
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.AddIdentityServices(builder.Configuration);
builder.Services.AddSingleton<OrderNotifyQueue>();
builder.Services.AddHostedService<OrderNotifyWorker>();

builder.Services.AddHttpClient("Melipayamak", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Melipayamak:BaseUrl"]);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("BaleMessenger", client =>
{
    var url = builder.Configuration["BaleMessenger:SafirBaseUrl"];
    if (!string.IsNullOrEmpty(url))
        client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddAuthentication()
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PineAI API", Version = "v1" });

    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Api-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "API key for order notification endpoint"
    });

    c.OperationFilter<ApiKeyOperationFilter>();
});

var app = builder.Build();

app.Services.InitializeDatabase();

app.UseResponseCompression();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<AppExceptionHandlerMiddleware>();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

