# PRD-012: Implementation plan — Actor runtime selection dashboard

**Status:** Phases 1–4 delivered (docs, API + catalog, dashboard UI, Tier A persistence + tests). Phase 5 (Tier B) not started.

## Phase 1: PRD and contracts

| Step | Action | Location |
| --- | --- | --- |
| 1.1 | Add `Project/prd-012/` readme + PRD + this plan | `Project/prd-012/` |
| 1.2 | Static `ActorRuntimeCatalog` keyed to factory ids | `AgctorSDK.Core/Runtime/ActorRuntimeCatalog.cs` |
| 1.3 | API DTOs | `AgctorSDK.Host/Models/RuntimeApiDtos.cs` |

## Phase 2: Persistence and API

| Step | Action | Location |
| --- | --- | --- |
| 2.1 | `IUserRuntimeSettingsService` + merge write to `appsettings.User.json` | `AgctorSDK.Host/Services/UserRuntimeSettingsService.cs` |
| 2.2 | `RuntimeController` — `GET` / `PUT` | `AgctorSDK.Host/Controllers/RuntimeController.cs` |
| 2.3 | Register service in DI | `AgctorSDK.Host/Program.cs` |
| 2.4 | `RuntimeCanonicalId` helper (adapter type → factory id) | `AgctorSDK.Host/Services/RuntimeCanonicalId.cs` |

## Phase 3: Dashboard UI

| Step | Action | Location |
| --- | --- | --- |
| 3.1 | Razor page + page model | `Pages/Dashboard/ActorRuntime.cshtml` |
| 3.2 | Page script: fetch, render catalog, form, save | `wwwroot/js/dashboard/actor-runtime-page.js` |
| 3.3 | Nav link | `Pages/Shared/_Layout.cshtml` |

## Phase 4: Quality and docs

| Step | Action | Location |
| --- | --- | --- |
| 4.1 | Unit tests: catalog ids | `AgctorSDK.Core.Tests/Runtime/ActorRuntimeCatalogTests.cs` |
| 4.2 | Tests: user settings merge; `GET /api/runtime` | `AgctorSDK.Host.IntegrationTests/` |
| 4.3 | Update Host endpoints / class diagrams when APIs added | `AgctorSDK.Host/docs/` |

## Phase 5: Tier B (optional, not delivered)

Delegating `IActorRuntimeAdapter`, swap API, concurrency rules, adapter lifecycle changes.

## Dependency order

1 → 2 → 3 → 4.
