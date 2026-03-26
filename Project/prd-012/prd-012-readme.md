# PRD-012 — Actor runtime selection dashboard

**Folder status:** Active — implementation tracks this specification.

## Documents

| File | Purpose |
| --- | --- |
| [prd-012-actor-runtime-dashboard.md](./prd-012-actor-runtime-dashboard.md) | Full PRD: goals, tiers (restart vs hot-swap), UX, API, acceptance criteria |
| [prd-012-implementation-plan.md](./prd-012-implementation-plan.md) | Phased delivery and key file locations |

## Relationship to other PRDs

- **PRD-006** exposed runtime name via `GET /api/config`. **PRD-012** adds a **dedicated Actor runtime** dashboard page, a **capability catalog**, `GET /api/runtime`, and **Tier A** persistence (`appsettings.User.json`) with **restart required** to apply.
- **PRD-010** pattern: user settings file and dashboard UX; runtime selection follows the same persistence approach as agent-type enablement.

## Implemented summary (v1)

- Dashboard page **`/Dashboard/ActorRuntime`**: current adapter (canonical id, adapter name, version, init, optional stats), next-boot settings from config, Proto fields, catalog cards with capabilities.
- **`GET /api/runtime`**: live adapter + `ActorRuntimeCatalog` entries for all factory runtimes.
- **`PUT /api/runtime`**: merges `Agctor:DefaultRuntime`, `Agctor:ProtoHost`, `Agctor:ProtoPort` into `appsettings.User.json`; response includes **`requiresRestart: true`**.
- **Tier B** (in-process hot swap) is **not** implemented; see PRD body.

## Key code locations

| Area | Location |
| --- | --- |
| Catalog / descriptors | `AgctorSDK.Core/Runtime/ActorRuntimeCatalog.cs` |
| Persist user runtime | `AgctorSDK.Host/Services/UserRuntimeSettingsService.cs` |
| HTTP API | `AgctorSDK.Host/Controllers/RuntimeController.cs` |
| DTOs | `AgctorSDK.Host/Models/RuntimeApiDtos.cs` |
| Dashboard UI | `Pages/Dashboard/ActorRuntime.cshtml`, `wwwroot/js/dashboard/actor-runtime-page.js` |
| Nav | `Pages/Shared/_Layout.cshtml` |
