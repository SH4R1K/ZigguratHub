using ChatServer;
using Microsoft.Extensions.Configuration.Memory;

var builder = WebApplication.CreateBuilder(args);

// Ключ OpenRouter берём из env OPENROUTER_API_KEY (приоритет над конфигом),
// чтобы он не попадал в appsettings.json и в git.
var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
if (!string.IsNullOrWhiteSpace(apiKey))
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        [OpenRouterOptions.SectionName + ":ApiKey"] = apiKey
    });
}

builder.Services.Configure<OpenRouterOptions>(builder.Configuration.GetSection(OpenRouterOptions.SectionName));

var app = builder.Build();

app.Run();