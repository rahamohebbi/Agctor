# PRD-006: Implementation Plan — Host Configuration Dashboard

This document breaks down the implementation of [PRD-006 (Host Configuration Dashboard)](./prd-006-dashboard.md) into ordered, actionable steps. All work is under **AgctorSDK.Host** unless noted.

---

## Phase 1: Host Configuration API

**Goal:** Expose `GET /api/config` returning full Host configuration (runtime, LLM, MCP, paths, background services, agent types, tools, scenarios).

### 1.1 DTOs and service contract

| Step | Action | Location |
|------|--------|----------|
| 1.1.1 | Add `HostConfigurationDto` and nested DTOs: `RuntimeConfigDto`, `LlmConfigDto`, `McpConfigDto`, `BackgroundServicesDto`, `ToolInfoDto` (or reuse existing `ToolInfo`), `ScenarioInfoDto`. Include all fields listed in PRD §1. | `AgctorSDK.Host/Models/HostConfigurationDto.cs` (or split into multiple files under Models/Dashboard/) |
| 1.1.2 | Define `IHostConfigurationService` with a single method, e.g. `Task<HostConfigurationDto> GetConfigurationAsync(CancellationToken ct)`. | `AgctorSDK.Host/Services/IHostConfigurationService.cs` |

### 1.2 Service implementation

| Step | Action | Location |
|------|--------|----------|
| 1.2.1 | Implement `HostConfigurationService` that injects `IConfiguration`, `IOptions<AgentTypeOptions>`, `IToolInvoker`, `IScenarioFactory`, and optionally `IActorRuntimeAdapter`. | `AgctorSDK.Host/Services/HostConfigurationService.cs` |
| 1.2.2 | In the implementation: read runtime name from config and from `IActorRuntimeAdapter.Name` if available; read `Agctor:LLM:*`, `Mcp:*`, `Agctor:GeneratedCodeRoot`, `TaskScoper:ScanInterval`, `TaskFlow:Interval`; get agent types from `AgentTypeOptions.AgentTypes`; call `IToolInvoker.GetAvailableToolsAsync()` and `GetToolInfoAsync(id)` for each; get scenarios from `IScenarioFactory.GetAvailableScenarios()` and `GetScenarioDescriptions()`. Map all into `HostConfigurationDto`. | Same file |
| 1.2.3 | Register `IHostConfigurationService` and `HostConfigurationService` in DI (Program.cs or an extension method). | `AgctorSDK.Host/Program.cs` |

### 1.3 API endpoint

| Step | Action | Location |
|------|--------|----------|
| 1.3.1 | Add `ConfigController` (or `DashboardController`) with `GET /api/config` that calls `IHostConfigurationService.GetConfigurationAsync()` and returns the DTO. | `AgctorSDK.Host/Controllers/ConfigController.cs` |
| 1.3.2 | Verify route: e.g. `[Route("api/[controller]")]` so URL is `/api/Config` (or name controller `DashboardController` for `/api/Dashboard` and action `Config`). Decide and document the final path (e.g. `/api/config`). | Same file |

### 1.4 Verification

- Manual: run Host, `GET https://localhost:port/api/config`, assert JSON contains runtime, llm, mcp, tools, scenarios, agentTypes.
- Add unit tests for `HostConfigurationService` with mocked IConfiguration, IOptions, IToolInvoker, IScenarioFactory (and optional IActorRuntimeAdapter). Assert structure and key values.
- Optionally add integration test in AgctorSDK.Host.IntegrationTests: GET /api/config, assert status 200 and presence of expected keys.

---

## Phase 2: Per-Agent Detail (Customizable agent views)

**Goal:** Expose `GET /api/agents/{id}/detail` with base agent info plus a type-specific `detail` object via provider pattern.

### 2.1 Abstractions (Core or Host)

| Step | Action | Location |
|------|--------|----------|
| 2.1.1 | Define `IAgentDetailProvider`: property `string AgentTypeName { get; }` and method `object? GetDetail(IAgent agent)`. Place in **AgctorSDK.Core** (Interfaces or a new Dashboard/Agents folder) so Host and future UIs can depend on it without pulling in agent implementations. | `AgctorSDK.Core/Interfaces/IAgentDetailProvider.cs` (or Core/Agents/Dashboard/) |
| 2.1.2 | Define `IAgentDetailProviderRegistry`: method `object? GetDetail(IAgent agent)` — resolves provider by `agent.GetType().Name` and invokes `GetDetail(agent)`; returns null or generic payload if no provider. | `AgctorSDK.Core/Interfaces/IAgentDetailProviderRegistry.cs` |
| 2.1.3 | Implement registry: `AgentDetailProviderRegistry` that takes `IEnumerable<IAgentDetailProvider>`, finds first provider where `AgentTypeName == agent.GetType().Name`, returns its `GetDetail(agent)`. If none, return a simple object (e.g. capabilities-only) or null and let the controller build a generic view. | In **Host**: `AgctorSDK.Host/Services/AgentDetailProviderRegistry.cs` (Core has no DI of multiple providers; Host wires them). |

**Alternative:** Put both interface and registry in Host if you prefer to avoid adding dashboard concepts to Core; then Host references Core only for `IAgent`.

### 2.2 Agent-specific providers (Host)

| Step | Action | Location |
|------|--------|----------|
| 2.2.1 | Implement `LLMAgentDetailProvider`: `AgentTypeName = "LLMAgent"`; `GetDetail(agent)` returns an object with `ollamaApiUrl` and `defaultModel` (read from `LLMAgent.GetConfiguredOllamaApiUrl()` and `GetConfiguredDefaultModel()` — static methods). Cast `agent` to LLMAgent only if needed for extra fields; otherwise use static config. | `AgctorSDK.Host/Services/AgentDetailProviders/LLMAgentDetailProvider.cs` |
| 2.2.2 | Implement `CoderAgentDetailProvider`: `AgentTypeName = "CoderAgent"`; `GetDetail(agent)` returns object with e.g. `tools: ["CodeEditorTool", "CompileTool", "TestRunnerTool"]` and optional `pipeline: "Edit → Compile → Test"` (fixed for CoderAgent). | `AgctorSDK.Host/Services/AgentDetailProviders/CoderAgentDetailProvider.cs` |
| 2.2.3 | (Optional) Add a **generic** provider or fallback in the registry: when no provider matches, return `new { capabilities = GetAgentCapabilities(agent) }` so the response always has a `detail` shape. Reuse or mirror logic from existing `AgentsController.GetAgentCapabilities`. | Same registry or `GenericAgentDetailProvider.cs` |

### 2.3 Registration and endpoint

| Step | Action | Location |
|------|--------|----------|
| 2.3.1 | Register all providers and `IAgentDetailProviderRegistry` in DI (Program.cs). Register each provider as `IAgentDetailProvider` (multiple registrations); registry takes `IEnumerable<IAgentDetailProvider>`. | `AgctorSDK.Host/Program.cs` |
| 2.3.2 | Add `GET /api/agents/{id}/detail`. Options: (A) New action on existing `AgentsController` (e.g. `GetAgentDetailAsync(string id)`), or (B) dedicated `AgentDetailController` with route `api/agents/{id}/detail`. Return type: e.g. `AgentDetailResponse` with Id, Type, Status, Metadata, Detail (object). Get agent from `IAgentRegistry.GetAgentByIdAsync(id)`; if null return 404; else call `IAgentDetailProviderRegistry.GetDetail(agent)` and build response. | `AgctorSDK.Host/Controllers/AgentsController.cs` (new action) or new controller |

### 2.4 Verification

- Unit tests: mock IAgent (e.g. LLMAgent), call LLMAgentDetailProvider.GetDetail, assert shape. Same for CoderAgentDetailProvider.
- Integration: after setting up code-generation-chain or code-graph-demo scenario, GET /api/agents/llm-agent/detail (or similar), assert 200 and detail.ollamaApiUrl / detail.defaultModel (or detail.tools for coder-agent).

---

## Phase 3: CodeGraph context and API

**Goal:** Expose `GET /api/codegraph/current` with actor tree (Solution → … → Method) and embedding store summary when CodeGraph scenario has been set up.

### 3.1 Context abstraction and DTO

| Step | Action | Location |
|------|--------|----------|
| 3.1.1 | Define `CodeGraphContext` DTO: e.g. `ActorTree` (root DTO matching ActorSerializer.ToDto shape: Id, ActorType, Name, PhysicalPath, Children) and `EmbeddingStoreSummary` (e.g. VectorCount). | In **Host** or **CodeGraph**: e.g. `AgctorSDK.Host/Models/CodeGraphContextDto.cs` or `AgctorSDK.CodeGraph/Models/CodeGraphContext.cs` |
| 3.1.2 | Define `ICodeGraphContextAccessor` with e.g. `CodeGraphContext? GetCurrent()` or `Task<CodeGraphContext?> GetCurrentAsync(CancellationToken ct)`. Implementation will be a singleton or scoped holder that the scenario sets. | `AgctorSDK.Host/Services/ICodeGraphContextAccessor.cs` (Host so API layer doesn’t reference CodeGraph internals) or in CodeGraph if you prefer. |

### 3.2 Implementation of the accessor

| Step | Action | Location |
|------|--------|----------|
| 3.2.1 | Implement a simple in-memory holder: e.g. `CodeGraphContextAccessor` with a volatile or lock-protected field `CodeGraphContext? _current` and methods `SetCurrent(CodeGraphContext?)` and `GetCurrent()`. Register as singleton. | `AgctorSDK.Host/Services/CodeGraphContextAccessor.cs` |
| 3.2.2 | Expose a way for CodeGraph scenario to set the context. Option A: inject `ICodeGraphContextAccessor` into `CodeGraphDemoScenario` and call `SetCurrent(...)` at the end of SetupAsync. Option B: define an interface in Host that CodeGraph implements (e.g. ICodeGraphContextSetter) and call from scenario. Prefer Option A with a setter interface so only the scenario (in Host) calls it. | `AgctorSDK.Host/Services/Scenarios/CodeGraphDemoScenario.cs` |

### 3.3 Populating context in CodeGraphDemoScenario

| Step | Action | Location |
|------|--------|----------|
| 3.3.1 | In CodeGraphDemoScenario.SetupAsync, after building the solution/project/file graph and embedding store: build `ActorTree` from the root (e.g. use `ActorSerializer.ToDto(solution)` — may require making ToDto accessible from Host or adding a small adapter in CodeGraph that returns a DTO). Build `EmbeddingStoreSummary` (e.g. ask InMemoryVectorStore for count if it exposes one; otherwise 0 or “available”). | `AgctorSDK.CodeGraph` may need to expose a public method that returns DTO from SolutionActor; or Host duplicates a minimal DTO builder. |
| 3.3.2 | Call `ICodeGraphContextAccessor.SetCurrent(context)`. If the accessor is in Host, inject it into CodeGraphDemoScenario (already in Host). | `AgctorSDK.Host/Services/Scenarios/CodeGraphDemoScenario.cs` |

### 3.4 API endpoint

| Step | Action | Location |
|------|--------|----------|
| 3.4.1 | Add `GET /api/codegraph/current` (e.g. `CodeGraphController` or a single action on Config/Dashboard controller). Call `ICodeGraphContextAccessor.GetCurrent()`. If null, return 404. Else return 200 with the context DTO. | `AgctorSDK.Host/Controllers/CodeGraphController.cs` (or extend ConfigController) |

### 3.5 Verification

- After running code-graph-demo scenario setup: GET /api/codegraph/current → 200, body has actor tree and embedding summary.
- Before any scenario or after a non-CodeGraph scenario: GET /api/codegraph/current → 404 (or 200 with empty payload, per design).

---

## Phase 4: Dashboard UI (Razor Pages + Tailwind + Flowbite + JavaScript)

**Goal:** Razor Pages for layout and navigation; **Tailwind CSS** and **Flowbite** for styling and components; JS to fetch APIs and render Overview, Agents list, Agent detail (type-specific), and CodeGraph view.

### 4.1 Enable Razor Pages and add Tailwind + Flowbite

| Step | Action | Location |
|------|--------|----------|
| 4.1.1 | Add Razor Pages support: `builder.Services.AddRazorPages()` (if not already present); `app.MapRazorPages()`. Ensure Razor runtime compilation or built-time views are available. | `AgctorSDK.Host/Program.cs` |
| 4.1.2 | Add a shared layout with **Tailwind CSS** and **Flowbite**: include Tailwind (e.g. `cdn.tailwindcss.com`) and Flowbite JS (e.g. `flowbite.min.js` from CDN) in `_Layout.cshtml`. Use a Flowbite-style navbar for links: Overview, Agents, CodeGraph, API (Swagger). Include script reference for dashboard.js. | `AgctorSDK.Host/Pages/Shared/_Layout.cshtml` |

### 4.2 Dashboard pages structure (Tailwind + Flowbite)

| Step | Action | Location |
|------|--------|----------|
| 4.2.1 | Create Index (overview) page with **Tailwind/Flowbite** styling: `Pages/Dashboard/Index.cshtml` and `Index.cshtml.cs`. Use Flowbite-style cards, badges, and grid layout; loading spinner while fetching. JS fetches GET /api/config and renders Runtime, LLM, MCP, Paths & Services, agent types, tools, and scenarios into card sections. | `AgctorSDK.Host/Pages/Dashboard/Index.cshtml`, `Index.cshtml.cs` |
| 4.2.2 | Create Agents list page with Tailwind/Flowbite: `Pages/Dashboard/Agents.cshtml` (and PageModel). Cards for “Registered types” (badges) and “Active agents” (list with links); JS fetches GET /api/config and GET /api/agents, renders list and types. | `AgctorSDK.Host/Pages/Dashboard/Agents.cshtml`, `Agents.cshtml.cs` |
| 4.2.3 | Create Agent detail page with Tailwind/Flowbite: `Pages/Dashboard/AgentDetail.cshtml` with route template `{id}`. Card layout for info and type-specific detail block; JS fetches GET /api/agents/{id}/detail and renders base info + type-specific block (switch on `data.type` or detail shape). | `AgctorSDK.Host/Pages/Dashboard/AgentDetail.cshtml`, `AgentDetail.cshtml.cs` |
| 4.2.4 | Create CodeGraph page with Tailwind/Flowbite: `Pages/Dashboard/CodeGraph.cshtml`. Cards for embedding store summary and actor tree; JS fetches GET /api/codegraph/current; on 200 render tree and summary; on 404 show “CodeGraph not active” in an alert/card. | `AgctorSDK.Host/Pages/Dashboard/CodeGraph.cshtml`, `CodeGraph.cshtml.cs` |

### 4.3 JavaScript module(s)

| Step | Action | Location |
|------|--------|----------|
| 4.3.1 | Add `wwwroot/js/dashboard.js` (or under `wwwroot/js/dashboard/` split by page). Functions: `fetchConfig()`, `fetchAgents()`, `fetchAgentDetail(id)`, `fetchCodeGraphCurrent()`. | `AgctorSDK.Host/wwwroot/js/dashboard.js` |
| 4.3.2 | Implement renderers: `renderOverview(config)`, `renderAgentsList(agents, config)`, `renderAgentDetail(data)` with type-based branching (LLMAgent, CoderAgent, generic), `renderCodeGraph(context)`. Use DOM APIs or minimal templating (e.g. innerHTML with escaped data or createElement). | Same file or separate `dashboard-renderers.js` |
| 4.3.3 | On each page load, call the appropriate fetch and render (e.g. on Index load call fetchConfig and renderOverview; on AgentDetail load call fetchAgentDetail(id) and renderAgentDetail). | Same file |

### 4.4 Navigation and links

| Step | Action | Location |
|------|--------|----------|
| 4.4.1 | From Agents list, link each agent to `/Dashboard/AgentDetail?id=llm-agent` (or route value `id`). From Overview or layout, link to Agents and CodeGraph. | Razor views + JS when building list |

### 4.5 Verification

- Open /Dashboard (or /Dashboard/Index), confirm Overview shows runtime, LLM, MCP, etc.
- Open /Dashboard/Agents, confirm agents list and registered types.
- Click an agent, confirm Agent detail page shows type-specific block (e.g. LLM URL/model for llm-agent).
- Open /Dashboard/CodeGraph; after running code-graph-demo, confirm tree and embedding summary; without scenario, confirm “not active” or 404 handling.

---

## Phase 5: Documentation and tests (ongoing)

| Step | Action | Location |
|------|--------|----------|
| 5.1 | Update AgctorSDK.Host/docs: add `/api/config`, `/api/agents/{id}/detail`, `/api/codegraph/current` to endpoints diagram (endpoints-diagram.mmd) and endpoints-diagram.md; brief note in README. | `AgctorSDK.Host/docs/` |
| 5.2 | Unit tests: HostConfigurationService (mocked deps); LLMAgentDetailProvider, CoderAgentDetailProvider (mock IAgent); AgentDetailProviderRegistry (multiple providers, unknown type). | `AgctorSDK.Host.Tests` or under existing test project for Host if present; otherwise AgctorSDK.Core.Tests for Core interfaces only. |
| 5.3 | Integration test: GET /api/config (200, structure); GET /api/agents/{id}/detail after scenario (200, detail shape); GET /api/codegraph/current after code-graph-demo (200), before (404). | `AgctorSDK.Host.IntegrationTests` |

---

## Dependency order

- Phase 1 must be done first (config API is used by dashboard and possibly by other phases only for documentation).
- Phase 2 can run in parallel with Phase 3 after Phase 1.
- Phase 4 depends on Phase 1, 2, and 3 (all endpoints available).
- Phase 5 can be done incrementally (e.g. docs and unit tests after each phase; integration tests after 2 and 3).

## Suggested implementation order

1. **Phase 1** (Config API) — full implementation and tests.
2. **Phase 2** (Agent detail) — interfaces, registry, LLM + Coder providers, endpoint, tests.
3. **Phase 3** (CodeGraph context) — DTO, accessor, scenario registration, endpoint, tests.
4. **Phase 4** (Dashboard UI) — Razor + JS, all four sections.
5. **Phase 5** — Finalize docs and any remaining tests.

This plan is aligned with PRD-006 and keeps the Host as the single deployment that serves both API and dashboard (Razor Pages + Tailwind CSS + Flowbite + JS). The dashboard UI uses **Tailwind CSS** and **Flowbite** (via CDN in the layout) so all pages share a consistent, component-based look without a Node build step.
