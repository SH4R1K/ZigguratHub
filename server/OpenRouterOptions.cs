namespace ChatServer;

/// <summary>Настройки прокси к OpenRouter (секция "OpenRouter" в конфигурации).</summary>
public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public string? ApiKey { get; set; }
    public string DefaultModel { get; set; } = "qwen/qwen3.8-27b:free";
    public string[] AllowedModels { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxMessages { get; set; } = 50;
    public int MaxContentLength { get; set; } = 8000;
}