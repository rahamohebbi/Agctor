# PRD-015 — Dashboard Ollama default model

**Folder status:** Active — specification and implementation plan for Host dashboard control of the global Ollama default model (PRD-015).

## Documents

| File | Purpose |
| --- | --- |
| [prd-015-agctor-prd.md](./prd-015-agctor-prd.md) | Goals, UX, APIs, persistence, errors, security (local dev) |
| [prd-015-implementation-plan.md](./prd-015-implementation-plan.md) | Delivery sequence, modules, acceptance, tests |

## Relationship to other PRDs

- **PRD-006**: Host configuration API (`GET /api/Config`); this PRD extends effective LLM reporting and adds model listing + default mutation.
- **PRD-010**: `appsettings.User.json` layering; PRD-015 persists `Agctor:LLM:DefaultModel` in the same user file pattern.

## Key code locations (post-implementation)

| Area | Location |
| --- | --- |
| LLM defaults (runtime) | `AgctorSDK.Agents/Agents/LLMAgent.cs` (`ConfigureDefaults`, `GetConfigured*`) |
| Host startup | `AgctorSDK.Host/Program.cs` |
| Ollama list + set default API | `AgctorSDK.Host/Controllers/LlmController.cs` |
| User persistence | `AgctorSDK.Host/Services/LlmUserSettingsService.cs` |
| Ollama `/api/tags` client | `AgctorSDK.Host/Services/OllamaModelCatalog.cs` |
| Effective config in dashboard | `AgctorSDK.Host/Services/HostConfigurationService.cs` |
| Dashboard UI | `AgctorSDK.Host/Pages/Dashboard/Index.cshtml` |
| Integration tests | `AgctorSDK.Host.IntegrationTests/LlmDashboardIntegrationTests.cs` |
