using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PineAI.Landing;
using PineAI.Landing.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp =>
{
    var baseUrl = builder.Configuration["ApiBaseUrl"];
    var apiKey = builder.Configuration["ApiKey"]!;
    var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    client.Timeout = TimeSpan.FromSeconds(60);
    return client;
});

builder.Services.AddScoped<ContactService>();
builder.Services.AddScoped<BlogService>();

var siteSettings = builder.Configuration.GetSection("Site").Get<SiteSettings>() ?? new SiteSettings();
builder.Services.AddSingleton(siteSettings);

await builder.Build().RunAsync();
