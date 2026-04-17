# PRD-015: Implementation plan — Dashboard Ollama default model

**Status:** Implemented per [prd-015-readme.md](./prd-015-readme.md).

## Sequence

1. **`ILlmUserSettingsService` + `LlmUserSettingsService`** — merge `Agctor:LLM:DefaultModel` into `appsettings.User.json` (pattern aligned with `UserProjectMemorySettingsService`).
2. **`IOllamaModelCatalog` + `OllamaModelCatalog`** — `HttpClient` `GET {base}/api/tags`, map to DTOs; unit-testable boundary for integration fakes.
3. **`LlmController`** — `GET models`, `PUT default-model`; on success call `LLMAgent.ConfigureDefaults(LLMAgent.GetConfiguredOllamaApiUrl(), model)`.
4. **`HostConfigurationService`** — populate `LlmConfigDto` from `LLMAgent.GetConfiguredOllamaApiUrl()` / `GetConfiguredDefaultModel()` so `/api/Config` matches runtime.
5. **Dashboard** — extend `Pages/Dashboard/Index.cshtml` LLM card (Tailwind + `fetch`, consistent with existing overview script).
6. **Tests** — `AgctorSDK.Host.IntegrationTests`: fake `IOllamaModelCatalog` where needed; assert `GET /api/Llm/models`, `PUT /api/Llm/default-model`, `GET /api/Config` coherence; restore `LLMAgent` static defaults after tests to avoid cross-class leakage.

## Risks

- **Static `LLMAgent` defaults**: integration tests must restore previous url/model after mutating.
- **Manual edit of `appsettings.User.json`**: `reloadOnChange` updates `IConfiguration` but not static `LLMAgent` until restart or PUT; documented in PRD; mitigated by showing effective values from `LLMAgent` in `/api/Config`.

## Module placement

| Area | Project / path |
| --- | --- |
| Controller, DTOs | `AgctorSDK.Host` |
| Ollama client, user settings | `AgctorSDK.Host/Services` |
| LLM static configuration | `AgctorSDK.Agents/Agents/LLMAgent.cs` (unchanged API) |
