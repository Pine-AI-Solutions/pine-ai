using Microsoft.EntityFrameworkCore;
using PineAI.Api.Landing.Auth;
using PineAI.Api.Landing.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<LandingBlogDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LandingBlogDb")));

builder.Services.AddAuthentication()
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("LandingPolicy", policy =>
    {
        policy.WithOrigins(
                "https://pineai.ir",
                "https://www.pineai.ir",
                "https://localhost:7280",
                "http://localhost:5280")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LandingBlogDbContext>();
    db.Database.Migrate();
}

app.UseCors("LandingPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
