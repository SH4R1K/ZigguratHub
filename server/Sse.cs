using System.Text.Json;

namespace ChatServer;

/// <summary>Запись SSE-событий в ответ: event: delta/done/error, data: JSON, пустая строка-разделитель.</summary>
public static class Sse
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteDeltaAsync(HttpResponse response, string content, CancellationToken ct) =>
        WriteEventAsync(response, "delta", new SseDelta { Content = content }, ct);

    public static Task WriteDoneAsync(HttpResponse response, string model, Usage? usage, CancellationToken ct) =>
        WriteEventAsync(response, "done", new SseDone { Model = model, Usage = usage }, ct);

    public static Task WriteErrorAsync(HttpResponse response, string type, string message, CancellationToken ct) =>
        WriteEventAsync(response, "error", new ApiError(type, message), ct);

    private static async Task WriteEventAsync(HttpResponse response, string eventName, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}