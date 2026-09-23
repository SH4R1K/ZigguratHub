using ChatServer;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Options;

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
    http.BaseAddress = new Uri(options.BaseUrl);
    // Таймаут управляется linked CTS в эндпоинте /api/chat.
    http.Timeout = Timeout.InfiniteTimeSpan;
});

var app = builder.Build();

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

    if (!request.Stream)
    {
        try
        {
            var chatResponse = await client.CompleteAsync(model, request.Messages, ct);
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

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(opts.TimeoutSeconds));

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