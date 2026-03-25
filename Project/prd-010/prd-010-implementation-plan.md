# PRD-010: Implementation plan — Agents dashboard overhaul

## Phase 1: PRD and configuration

| Step | Action | Location |
| --- | --- | --- |
| 1.1 | Add `appsettings.User.json` to `.gitignore`; document path in PRD readme. | Root `.gitignore`, `Project/prd-010/` |
| 1.2 | Add `AddJsonFile("appsettings.User.json", optional: true, reloadOnChange: true)` after `CreateBuilder`. | `AgctorSDK.Host/Program.cs` |
| 1.3 | Default `Agctor:Dashboard:ScenarioName` in `appsettings.json`. | `AgctorSDK.Host/appsettings.json` |
| 1.4 | Implement merge read/write for `Agctor:AgentTypeEnablement` and dashboard scenario name. | `AgctorSDK.Host/Services/AgentTypeEnablementService.cs` |

## Phase 2: API and DTOs

| Step | Action | Location |
| --- | --- | --- |
| 2.1 | Extend `HostConfigurationDto` with `DashboardScenarioName` and `AgentTypeEnablement`. | `AgctorSDK.Host/Models/HostConfigurationDto.cs` |
| 2.2 | Populate new fields in `HostConfigurationService`. | `AgctorSDK.Host/Services/HostConfigurationService.cs` |
| 2.3 | `PUT /api/agents/types/{typeName}/enabled` + validate against registered types. | `AgctorSDK.Host/Controllers/AgentsController.cs` |
| 2.4 | Optional: `POST /api/Test/setup-scenario` uses config scenario when body omits name. | `AgctorSDK.Host/Controllers/TestController.cs` |

## Phase 3: Scenarios and startup

| Step | Action | Location |
| --- | --- | --- |
| 3.1 | Inject `IAgentTypeEnablementService` into scenarios; skip spawns for disabled **known** types. | `CodeGenerationChainScenario.cs`, `CodeGraphDemoScenario.cs` |
| 3.2 | Optional: respect enablement when spawning startup `SessionCoordinatorAgent` in `Program.cs`. | `Program.cs` |

## Phase 4: UI

| Step | Action | Location |
| --- | --- | --- |
| 4.1 | Replace inline script with `wwwroot/js/dashboard/agents-page.js`. | `Agents.cshtml`, new JS file |
| 4.2 | Flowbite table, toggles, single Apply, refresh, error banner. | Same |

## Phase 5: Quality gate

| Step | Action | Location |
| --- | --- | --- |
| 5.1 | Unit tests for enablement resolution and optional file merge helpers. | `AgctorSDK.Core.Tests` or Host test project |
| 5.2 | Integration test for PUT enablement + GET config. | `AgctorSDK.Host.IntegrationTests` |
| 5.3 | Build solution; unit tests; integration tests. | Solution |

## Documentation

- Update `AgctorSDK.Host/docs/endpoints-diagram.*` if new routes are added.
- Update `AgctorSDK.Host/docs/class-diagram.*` for new services/DTOs.

## Dependency order

1 → 2 → 3 → 4 → 5.
