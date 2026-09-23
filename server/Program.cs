using ChatServer;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

// Env-переменные OPENROUTER_* имеют приоритет над appsettings.
// Ключ читаем только из env, чтобы он не попадал в appsettings.json и git.
var envOverrides = new Dictionary<string, string?>();
foreach (var (envVar, option) in new (string, string)[]
{
    ("OPENROUTER_API_KEY", "ApiKey"),
    ("OPENROUTER_MODEL", "DefaultModel"),
    ("OPENROUTER_BASE_URL", "BaseUrl"),
    ("OPENROUTER_USE_PROXY", "UseProxy"),
    ("OPENROUTER_PROXY_URL", "ProxyUrl"),
})
{
    var value = Environment.GetEnvironmentVariable(envVar);
    if (!string.IsNullOrWhiteSpace(value))
        envOverrides[$"{OpenRouterOptions.SectionName}:{option}"] = value;
}

if (envOverrides.Count > 0)
    builder.Configuration.AddInMemoryCollection(envOverrides);

builder.Services.Configure<OpenRouterOptions>(builder.Configuration.GetSection(OpenRouterOptions.SectionName));

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddHttpClient<OpenRouterClient>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    // Таймаут управляется linked CTS в эндпоинте /api/chat.
    http.Timeout = Timeout.InfiniteTimeSpan;
})
.ConfigurePrimaryHttpMessageHandler(sp =>
{
    var options = sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value;
    var handler = new SocketsHttpHandler();

    // Прокси включается только явно (UseProxy=true). По умолчанию — как раньше, без прокси.
    ValidateProxyOptions(options);

    if (options.UseProxy)
        handler.Proxy = CreateProxy(options.ProxyUrl!);

    return handler;
});

// Валидация конфигурации прокси: вызывается при старте (fail fast) и при создании клиента.
static void ValidateProxyOptions(OpenRouterOptions options)
{
    if (!options.UseProxy)
        return;

    if (string.IsNullOrWhiteSpace(options.ProxyUrl))
        throw new InvalidOperationException("OpenRouter: UseProxy=true, но OPENROUTER_PROXY_URL не задан.");

    var uri = new Uri(options.ProxyUrl, UriKind.Absolute);
    var supportedSchemes = new[] { "http", "https", "socks4", "socks4a", "socks5" };
    if (!supportedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            $"OpenRouter: неподдерживаемая схема прокси \"{uri.Scheme}\". " +
            "Поддерживаются: http, https, socks4, socks4a, socks5. " +
            "socks5h не поддерживается в .NET 10, используйте socks5.");
}

// Адрес собираем без userinfo: uri.Authority содержит userinfo, его использовать нельзя.
static WebProxy CreateProxy(string proxyUrl)
{
    var uri = new Uri(proxyUrl, UriKind.Absolute);
    var address = new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;
    var proxy = new WebProxy(address);
    if (!string.IsNullOrEmpty(uri.UserInfo))
    {
        var parts = uri.UserInfo.Split(':', 2);
        proxy.Credentials = new NetworkCredential(parts[0], parts.Length > 1 ? parts[1] : string.Empty);
    }
    return proxy;
}

var app = builder.Build();

// Fail fast: ошибка конфигурации прокси валит сервер при старте, а не при первом запросе.
ValidateProxyOptions(app.Services.GetRequiredService<IOptions<OpenRouterOptions>>().Value);

app.UseCors();

static int ErrorStatus(string type) => type switch
{
    "validation" or "model_not_allowed" => StatusCodes.Status400BadRequest,
    "config" => StatusCodes.Status500InternalServerError,
    "quota_limit" => StatusCodes.Status429TooManyRequests,
    "timeout" => StatusCodes.Status504GatewayTimeout,
    "auth" or "network" or "upstream" => StatusCodes.Status502BadGateway,
    _ => StatusCodes.Status500InternalServerError
};

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/models", (IOptions<OpenRouterOptions> options) =>
    Results.Ok(new { models = options.Value.AllowedModels }));

app.MapPost("/api/chat", async (ChatRequest? request, OpenRouterClient client, IOptions<OpenRouterOptions> options, HttpContext context, CancellationToken ct) =>
{
    var opts = options.Value;

    if (request is null)
        return Results.Json(new ApiError("validation", "Тело запроса не может быть пустым."), statusCode: StatusCodes.Status400BadRequest);

    var validationError = request.Validate(opts);
    if (validationError is not null)
        return Results.Json(validationError, statusCode: StatusCodes.Status400BadRequest);

    if (string.IsNullOrWhiteSpace(opts.ApiKey))
        return Results.Json(new ApiError("config", "Ключ OpenRouter не настроен на сервере: задайте переменную окружения OPENROUTER_API_KEY."), statusCode: StatusCodes.Status500InternalServerError);

    var model = request.Model ?? opts.DefaultModel;

    // Таймаут и отмена клиента действуют на оба режима (stream и non-stream).
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(opts.TimeoutSeconds));

    if (!request.Stream)
    {
        try
        {
            var chatResponse = await client.CompleteAsync(model, request.Messages, cts.Token);
            return Results.Ok(chatResponse);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // клиент оборвал соединение — отвечать некому
            return Results.Empty;
        }
        catch (OperationCanceledException)
        {
            return Results.Json(new ApiError("timeout", "Превышено время ожидания ответа модели."), statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (OpenRouterException ex)
        {
            return Results.Json(new ApiError(ex.Type, ex.Message), statusCode: ErrorStatus(ex.Type));
        }
        catch (Exception)
        {
            return Results.Json(new ApiError("internal", "Внутренняя ошибка сервера."), statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // SSE-стриминг: статус уже 200, ошибки после начала стрима пишем событиями error.
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    try
    {
        await client.StreamAsync(model, request.Messages, context.Response, cts.Token);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // клиент оборвал соединение — молча завершаем стрим
    }
    catch (OperationCanceledException)
    {
        await Sse.WriteErrorAsync(context.Response, "timeout", "Превышено время ожидания ответа модели.", CancellationToken.None);
    }
    catch (OpenRouterException ex)
    {
        await Sse.WriteErrorAsync(context.Response, ex.Type, ex.Message, CancellationToken.None);
    }
    catch (Exception)
    {
        await Sse.WriteErrorAsync(context.Response, "internal", "Внутренняя ошибка сервера.", CancellationToken.None);
    }

    return Results.Empty;
});

app.Run();