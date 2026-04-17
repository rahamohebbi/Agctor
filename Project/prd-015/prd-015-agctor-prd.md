# PRD-015: Dashboard Ollama default model

## 1. Overview

Operators running AGCTOR Host locally with [Ollama](https://ollama.com/) need a simple way to see **which models are installed** on their machine and to **set the global default model** used by Host features that call `LLMAgent` defaults (project-memory pipeline, agents constructed with the single-argument constructor, etc.) without editing JSON by hand.

## 2. Goals

1. From `/Dashboard` (Host configuration overview), list models returned by Ollama `GET /api/tags` for the configured `Agctor:LLM:OllamaApiUrl`.
2. Allow selecting one model and setting it as the **default** used at runtime (`LLMAgent.ConfigureDefaults`).
3. Persist the choice to `appsettings.User.json` under `Agctor:LLM:DefaultModel` so the next Host restart uses the same default (existing config layering in `Program.cs`).
4. Keep `GET /api/Config` **effective** LLM fields aligned with what generation actually uses (`GetConfiguredOllamaApiUrl` / `GetConfiguredDefaultModel`).

## 3. Non-goals

1. Editing Ollama base URL from the dashboard (remains `appsettings` / user JSON only).
2. Per-agent or per-scenario model selection.
3. Pulling or deleting models (`ollama pull`, `ollama rm`) from the UI.

## 4. UX

- On the **LLM** card of the dashboard overview:
  - Show current Ollama URL and default model (from `/api/Config`).
  - **Refresh models**: calls Host API; shows loading and error states if Ollama is unreachable.
  - **Dropdown** (or equivalent) populated with local model names.
  - **Set as default**: persists and applies; show success or validation error.
- Copy should clarify that this affects the **global Host default**, not individual agent YAML overrides (if any exist elsewhere).

## 5. APIs (Host)

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/api/Llm/models` | Proxies to `{OllamaApiUrl}/api/tags`; returns a JSON array of `{ name, size?, modifiedAt? }` (minimal fields for UI). |
| `PUT` | `/api/Llm/default-model` | Body: `{ "model": "<id>" }`. Validates non-empty model. Optionally, when Ollama is reachable, warn in response if the name is not in `/api/tags` but still allow save. Persists to `appsettings.User.json`, then calls `LLMAgent.ConfigureDefaults(url, model)`. |

Errors:

- **502** or **503** when listing models if Ollama HTTP fails (message for operator).
- **400** when model string is missing or whitespace.

## 6. Persistence

- Merge into `appsettings.User.json` at Host `ContentRootPath` (same file as PRD-010 project root overrides).
- Only the `Agctor:LLM:DefaultModel` key is written by this feature (preserve other keys).

## 7. Security

- Intended for **local development** dashboards. No authentication in scope; document that exposing Host on a network requires separate hardening.

## 8. Acceptance

1. With Ollama running and at least one model pulled, `/api/Llm/models` returns HTTP 200 and non-empty `models` when configured URL is correct.
2. `PUT /api/Llm/default-model` updates `LLMAgent.GetConfiguredDefaultModel()` immediately and subsequent `GET /api/Config` reports the new `llm.defaultModel`.
3. After Host restart, the same default is read from `appsettings.User.json` (merged with base `appsettings.json`).
4. Dashboard shows the same behavior without manual file edits.
