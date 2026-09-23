namespace ChatServer;

/// <summary>Тело запроса POST /api/chat.</summary>
public sealed class ChatRequest
{
    /// <summary>Верхняя граница для ответов модели в истории: их генерирует нейронка и они бывают длиннее лимита ввода пользователя.</summary>
    private const int MaxAssistantContentLength = 50_000;

    public string? Model { get; set; }
    public List<ChatMessage> Messages { get; set; } = [];
    public bool Stream { get; set; } = true;

    /// <summary>Проверяет запрос по ограничениям из настроек; возвращает ошибку или null.</summary>
    public ApiError? Validate(OpenRouterOptions options)
    {
        if (Messages is null || Messages.Count == 0)
            return new ApiError("validation", "Передайте хотя бы одно сообщение.");

        if (Messages.Count > options.MaxMessages)
            return new ApiError("validation", $"Слишком много сообщений: максимум {options.MaxMessages}.");

        foreach (var message in Messages)
        {
            if (message.Role is not ("user" or "assistant"))
                return new ApiError("validation", $"Недопустимая роль \"{message.Role}\": допустимы user и assistant.");

            if (string.IsNullOrWhiteSpace(message.Content))
                return new ApiError("validation", "Текст сообщения не может быть пустым.");

            var maxContentLength = message.Role == "user" ? options.MaxContentLength : MaxAssistantContentLength;
            if (message.Content.Length > maxContentLength)
                return new ApiError("validation", $"Сообщение слишком длинное: максимум {maxContentLength} символов.");
        }

        if (Model is not null && !options.AllowedModels.Contains(Model))
            return new ApiError("model_not_allowed", $"Модель \"{Model}\" недоступна. Список доступных моделей: GET /api/models.");

        return null;
    }
}

public sealed class ChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>Ответ POST /api/chat при stream=false.</summary>
public sealed class ChatResponse
{
    public string Content { get; set; } = "";
    public string Model { get; set; } = "";
    public Usage? Usage { get; set; }
}

public sealed class Usage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

/// <summary>SSE-событие delta: очередной фрагмент текста ответа.</summary>
public sealed class SseDelta
{
    public string Content { get; set; } = "";
}

/// <summary>SSE-событие done: стрим завершён, модель и статистика токенов.</summary>
public sealed class SseDone
{
    public string Model { get; set; } = "";
    public Usage? Usage { get; set; }
}

/// <summary>Структурированная ошибка API: SSE-событие error и тело ответа при ошибке.</summary>
public sealed class ApiError
{
    public string Type { get; set; }
    public string Message { get; set; }

    public ApiError(string type, string message)
    {
        Type = type;
        Message = message;
    }
}