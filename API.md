# API сервера чата

Прокси к OpenRouter: ключ живёт только на сервере, в браузер не попадает.
Базовый URL: `http://localhost:5080` (профиль `http` в `launchSettings.json`).

## Эндпоинты

| Метод | Путь          | Описание                                        |
|-------|---------------|-------------------------------------------------|
| GET   | `/api/health` | Проверка живости сервера.                       |
| GET   | `/api/models` | Список разрешённых моделей (белый список).      |
| POST  | `/api/chat`   | Диалог с моделью: стриминг SSE или обычный JSON.|

## GET /api/health

Ответ `200`:

```json
{ "status": "ok" }
```

## GET /api/models

Ответ `200` — массив моделей из конфигурации `OpenRouter:AllowedModels`:

```json
{
  "models": [
    "cohere/north-mini-code:free",
    "qwen/qwen3.8-27b:free",
    "z-ai/glm-5.2:free"
  ]
}
```

## POST /api/chat

### Тело запроса

```json
{
  "model": "qwen/qwen3.8-27b:free",
  "messages": [
    { "role": "user", "content": "Привет!" }
  ],
  "stream": true
}
```

| Поле      | Тип      | Обязательное | Описание                                                        |
|-----------|----------|--------------|-----------------------------------------------------------------|
| `model`   | string   | нет          | ID модели из `/api/models`. По умолчанию — `OpenRouter:DefaultModel`. |
| `messages`| array    | да           | История диалога: 1–50 сообщений. Роли: `user`, `assistant`.     |
| `stream`  | boolean  | нет          | `true` (по умолчанию) — SSE-стриминг, `false` — обычный JSON.   |

Ограничения: текст сообщения непустой и не длиннее 8000 символов
(`OpenRouter:MaxContentLength`), максимум 50 сообщений (`OpenRouter:MaxMessages`).

### stream=true (по умолчанию)

Ответ `200` с `Content-Type: text/event-stream`. События:

```
event: delta
data: {"content":"Привет"}

event: delta
data: {"content":"!"}

event: done
data: {"model":"qwen/qwen3.8-27b:free","usage":{"promptTokens":12,"completionTokens":5,"totalTokens":17}}

```

| Событие | data                                                        | Когда                          |
|---------|-------------------------------------------------------------|--------------------------------|
| `delta` | `{ "content": "..." }`                                      | Каждый фрагмент текста ответа. |
| `done`  | `{ "model": "...", "usage": { ... } \| null }`              | Стрим завершён.                |
| `error` | `{ "type": "...", "message": "..." }`                       | Ошибка после начала стрима.    |

`usage` может отсутствовать (`null`), если модель не вернула статистику токенов.
Сервер явно запрашивает usage у OpenRouter (`stream_options.include_usage`),
поэтому в `done` он приходит для моделей, поддерживающих этот флаг.

### stream=false

Ответ `200`:

```json
{
  "content": "Привет! Чем могу помочь?",
  "model": "qwen/qwen3.8-27b:free",
  "usage": { "promptTokens": 12, "completionTokens": 5, "totalTokens": 17 }
}
```

### Ошибки

Формат ошибки — `{ "type": "...", "message": "..." }`.
При `stream=true` ошибка после начала стрима приходит SSE-событием `error`
(HTTP-статус уже `200`); до начала стрима и при `stream=false` — обычный JSON
с HTTP-статусом из таблицы.

| type              | HTTP-статус | Когда возникает                                                        |
|-------------------|-------------|------------------------------------------------------------------------|
| `validation`      | 400         | Пустое тело, нет сообщений, недопустимая роль, пустой/слишком длинный текст. |
| `model_not_allowed`| 400        | `model` не входит в белый список `/api/models`.                        |
| `config`          | 500         | Ключ OpenRouter не задан на сервере.                                   |
| `auth`            | 502         | OpenRouter вернул 401/403 (неверный ключ).                             |
| `quota_limit`     | 429         | OpenRouter вернул 402/429 (лимит запросов или средств).                |
| `timeout`         | 504         | Превышен `OpenRouter:TimeoutSeconds` (по умолчанию 120 с).             |
| `network`         | 502         | Сетевая ошибка при обращении к OpenRouter.                             |
| `upstream`        | 502         | OpenRouter вернул 400/404/5xx.                                         |
| `internal`        | 500         | Непредвиденная ошибка сервера.                                         |

## Примеры curl

```bash
# Проверка живости
curl http://localhost:5080/api/health

# Список моделей
curl http://localhost:5080/api/models

# Стриминг (SSE)
curl -N -X POST http://localhost:5080/api/chat \
  -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"user","content":"Привет!"}]}'

# Без стриминга
curl -X POST http://localhost:5080/api/chat \
  -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"user","content":"Привет!"}],"stream":false}'
```

## CORS

Разрешённые origin'ы — из конфигурации `Cors:AllowedOrigins`
(по умолчанию `["http://localhost:5173"]` — dev-сервер Vite).

## Запуск и конфигурация

```bash
dotnet run --project server
```

Сервер поднимется на `http://localhost:5080`.

Переменные окружения:

| Переменная             | Описание                                                        |
|------------------------|-----------------------------------------------------------------|
| `OPENROUTER_API_KEY`   | Ключ OpenRouter. Обязателен для `/api/chat`. Не хранится в конфиге и git. |
| `OPENROUTER_MODEL`     | Модель по умолчанию (переопределяет `OpenRouter:DefaultModel`). |
| `OPENROUTER_BASE_URL`  | Базовый URL OpenRouter (по умолчанию `https://openrouter.ai/api/v1`). |

Секции `appsettings.json`:

- `OpenRouter` — `BaseUrl`, `DefaultModel`, `AllowedModels`, `TimeoutSeconds`,
  `MaxMessages`, `MaxContentLength`. `ApiKey` сюда не кладётся — только env.
- `Cors:AllowedOrigins` — список разрешённых origin'ов.