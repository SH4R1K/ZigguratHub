using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ChatServer;

/// <summary>Прокси к OpenRouter: POST {BaseUrl}/chat/completions, стриминг SSE и обычный JSON.</summary>
public sealed class OpenRouterClient
{
    private static readonly JsonSerializerOptions OpenRouterJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly OpenRouterOptions _options;

    public OpenRouterClient(HttpClient http, IOptions<OpenRouterOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>Полный (не стриминговый) запрос: возвращает собранный ответ и usage.</summary>
    public async Task<ChatResponse> CompleteAsync(string model, IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        using var request = CreateRequest(model, messages, stream: false);
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);

        if (!response.IsSuccessStatusCode)
            throw await CreateUpstreamErrorAsync(response, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var payload = JsonSerializer.Deserialize<OpenRouterCompletion>(body, OpenRouterJson)
            ?? throw new OpenRouterException("internal", "Пустой ответ от OpenRouter.");

        return new ChatResponse
        {
            Content = payload.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty,
            Model = payload.Model ?? model,
            Usage = MapUsage(payload.Usage)
        };
    }

    /// <summary>Стриминговый запрос: каждый delta пишется в SSE сразу, в конце — событие done.</summary>
    public async Task StreamAsync(string model, IReadOnlyList<ChatMessage> messages, HttpResponse response, CancellationToken ct)
    {
        using var request = CreateRequest(model, messages, stream: true);
        using var upstream = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!upstream.IsSuccessStatusCode)
            throw await CreateUpstreamErrorAsync(upstream, ct);

        await using var stream = await upstream.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? responseModel = null;
        Usage? usage = null;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
                break;

            var chunk = JsonSerializer.Deserialize<OpenRouterChunk>(data, OpenRouterJson);
            if (chunk is null)
                continue;

            responseModel ??= chunk.Model;

            var delta = chunk.Choices?.FirstOrDefault()?.Delta?.Content;
            if (!string.IsNullOrEmpty(delta))
                await Sse.WriteDeltaAsync(response, delta, ct);

            if (chunk.Usage is not null)
                usage = MapUsage(chunk.Usage);
        }

        await Sse.WriteDoneAsync(response, responseModel ?? model, usage, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken ct)
    {
        try
        {
            return await _http.SendAsync(request, completionOption, ct);
        }
        catch (HttpRequestException)
        {
            throw new OpenRouterException("network", "Не удалось связаться с OpenRouter: проверьте сеть и настройки сервера.");
        }
    }

    private HttpRequestMessage CreateRequest(string model, IReadOnlyList<ChatMessage> messages, bool stream)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream
        }, OpenRouterJson);

        var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Referrer = new Uri("http://localhost:5080");
        request.Headers.Add("X-Title", "Testovoe Chat");

        return request;
    }

    private async Task<OpenRouterException> CreateUpstreamErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var type = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "auth",
            HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests => "quota_limit",
            _ => "upstream"
        };

        var message = type switch
        {
            "auth" => "Не удалось авторизоваться в OpenRouter: проверьте ключ API.",
            "quota_limit" => "Достигнут лимит запросов или средств: попробуйте позже или выберите другую модель.",
            _ => "Сервис OpenRouter временно недоступен, попробуйте ещё раз."
        };

        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            var payload = JsonSerializer.Deserialize<OpenRouterErrorEnvelope>(body, OpenRouterJson);
            if (!string.IsNullOrWhiteSpace(payload?.Error?.Message))
                message = $"OpenRouter: {payload.Error.Message}";
        }
        catch (Exception)
        {
            // тело ответа не JSON — оставляем общее сообщение
        }

        return new OpenRouterException(type, message);
    }

    private static Usage? MapUsage(OpenRouterUsage? usage) =>
        usage is null ? null : new Usage
        {
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
            TotalTokens = usage.TotalTokens
        };

    private sealed class OpenRouterCompletion
    {
        public string? Model { get; set; }
        public List<OpenRouterCompletionChoice>? Choices { get; set; }
        public OpenRouterUsage? Usage { get; set; }
    }

    private sealed class OpenRouterCompletionChoice
    {
        public OpenRouterMessage? Message { get; set; }
    }

    private sealed class OpenRouterMessage
    {
        public string? Content { get; set; }
    }

    private sealed class OpenRouterChunk
    {
        public string? Model { get; set; }
        public List<OpenRouterChunkChoice>? Choices { get; set; }
        public OpenRouterUsage? Usage { get; set; }
    }

    private sealed class OpenRouterChunkChoice
    {
        public OpenRouterDelta? Delta { get; set; }
    }

    private sealed class OpenRouterDelta
    {
        public string? Content { get; set; }
    }

    private sealed class OpenRouterUsage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
    }

    private sealed class OpenRouterErrorEnvelope
    {
        public OpenRouterError? Error { get; set; }
    }

    private sealed class OpenRouterError
    {
        public string? Message { get; set; }
    }
}

/// <summary>Ошибка прокси к OpenRouter с типом для ApiError.</summary>
public sealed class OpenRouterException : Exception
{
    public string Type { get; }

    public OpenRouterException(string type, string message) : base(message)
    {
        Type = type;
    }
}