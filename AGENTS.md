# Project Instructions

> Основано на `AGENTS.project.md.template`. Заполняй project-specific деталями.
> Общие знания о C#/.NET и OpenCode — в глобальных скиллах, не здесь.

## Project overview

Одностраничный веб-чат с языковой моделью (OpenRouter, `:free` модели).
Монолит-монorepo: `server/` (ASP.NET Core minimal API — прокси к OpenRouter, держит ключ на сервере) + `web/` (React + TypeScript + Vite).

## Architecture

- `server/` — ASP.NET Core 10 (minimal API), один проект без лишних слоёв: `POST /api/chat` проксирует стриминговый SSE OpenRouter, нормализуя события (`delta`/`done`/`error`) для фронта.
- `web/` — React + Vite, стриминг через `fetch` + `ReadableStream`, `AbortController` для «Стоп».
- Ключ OpenRouter живёт только в `OPENROUTER_API_KEY` (env/user-secrets), никогда не попадает в браузер.
- История диалога — `localStorage` на клиенте, сервер stateless (обоснование в README).
- Паттерны: минимальные, без DDD/CQRS/Repository; бизнес-логика — сервисы в `server/`, состояние чата — хук `useChat` в `web/`.

## Development

- Git-workflow: `gitworkflow.md` — ветки, этапы, формат коммитов. Обязателен для всех агентов при коммитах.
- Бэк: `dotnet build` / `dotnet run --project server` / `dotnet watch`.
- Фронт: `npm install --prefix web` / `npm run dev --prefix web` (Vite, порт 5173).
- Конфиг: `server/appsettings.json` + env (`OPENROUTER_API_KEY`, `OPENROUTER_MODEL`, `OPENROUTER_BASE_URL`).

## Testing

- `dotnet test` — тестовый проект в `server/` (xUnit), если добавлен.
- Фронт: `npm run lint` + `npm run typecheck` (TS), юнит-тесты — при наличии.
- Интеграции с OpenRouter руками: проверка стриминга, «Стоп», ошибки 429/таймаут.

## Conventions

- .NET: nullable reference types включены; async/await без блокирующих вызовов; `CancellationToken` пробрасывается.
- Naming: PascalCase (C#), camelCase (TS/React); структура файлов — по папкам выше.
- Без новых NuGet/npm-зависимостей без согласования (кроме уже заложенных: `react-markdown` + `remark-gfm`, xUnit).
- Ошибки: структурированные (`error{type,message}`), человеческие сообщения на UI, никаких вечных спиннеров.

## Important constraints

- TFM: `net10.0` (установлен .NET 10 SDK).
- Доступность: семантика (`form`, `main`, `ul/li`, `aria-live`), Enter — отправка, Shift+Enter — перенос, Esc — стоп; focus — кастомный `:focus-visible`.
- Ключ OpenRouter не должен появиться в бандле, запросах страницы или коммитах.
- Не коммитить секреты/`.env` (кроме `.env.example`), `bin/`, `obj/`, `node_modules/`.

## Definition of Done

- [ ] реализовано поведение согласно требованиям TASK.md;
- [ ] сборка бэка и фронта проходит;
- [ ] добавлены/обновлены тесты (если уместно);
- [ ] тесты проходят;
- [ ] диф маленький, без coupled/unrelated изменений;
- [ ] README обновлён (запуск, ключевые решения, ИИ-лог).