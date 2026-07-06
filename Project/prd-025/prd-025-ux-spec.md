# PRD-025: UX spec — RAG providers dashboard

**Route:** `/Dashboard/RagProviders`  
**Nav label:** RAG providers (top bar, adjacent to **Actor runtime**)

Mirrors PRD-012 Actor runtime patterns: same card grid, config form, Docker sidecar panel, terminal command panel, health refresh, success banner.

## 1. Page header

- **Title:** RAG providers
- **Subtitle:** Select an external retrieval backend for Project Memory `rag` / `graph_rag` strategies. Settings persist in `appsettings.User.json` (per machine, not in git).
- **Banner** (hidden by default): green success after save — “Saved **LightRAG** as default provider.”

## 2. Sections (top to bottom)

### 2.1 Current provider card

| Field | Source |
| --- | --- |
| Provider id | Active configured provider |
| Transport | Rest / McpHttp |
| Health badge | `GET /api/rag-providers/health` |
| Last query | Optional “Test query succeeded 2m ago” |

**Refresh** button — same styling as Actor runtime health refresh.

### 2.2 Mismatch warning (amber)

Shown when saved provider ≠ reachable sidecar, e.g.:

> Saved **Cognee** but Docker service is **stopped**. Start the sidecar or switch to **Markdown only**.

### 2.3 Select provider (card grid)

Three cards in v1 (responsive `md:grid-cols-3`):

| Card | Badge | Footer hint |
| --- | --- | --- |
| **Markdown only** | `None` | No external RAG; uses on-disk markdown strategies only. |
| **LightRAG** | `graph_rag`, `rag` | Docker sidecar · REST API |
| **Cognee** | `graph_rag`, memory | Docker sidecar · MCP HTTP |

Click card → selects provider → scroll/focus config form → auto-save optional (match Actor runtime: **click saves and applies** for provider id; config fields use **Save** button).

Selected card: blue border (same classes as Actor runtime).

Each card shows: display name, mono id, maturity (`supported` / `experimental`), 2-line summary, capability tags as pills (`local_docker`, `graph`, `hybrid`, `mcp`).

### 2.4 Configuration form

Shown for selected provider (hidden for `None`).

**LightRAG fields:**

| Label | Key | Type |
| --- | --- | --- |
| API base URL | `baseUrl` | text, default `http://127.0.0.1:9621` |
| API key | `apiKey` | password, optional |
| Default query mode | `defaultMode` | select: Hybrid / Local / Global / Naive |
| Transport | `transport` | select: Rest (v1 only) |

**Cognee fields:**

| Label | Key | Type |
| --- | --- | --- |
| MCP base URL | `baseUrl` | text, default `http://127.0.0.1:8000` |
| MCP path | `mcpPath` | text, default `/mcp` |
| Search type | `searchType` | select: RAG_COMPLETION / GRAPH_COMPLETION |
| LLM API key | `llmApiKey` | password, help: “Required for cognify; passed to Docker env.” |

**Save** → `PUT /api/rag-providers` → success banner.

### 2.5 Docker sidecar panel

Visible when selected provider has `RequiresDocker: true` (LightRAG, Cognee).

Reuse Actor runtime layout:

| Element | Id pattern |
| --- | --- |
| Panel root | `#rag-docker-panel` `data-docker-provider-id="LightRAG"` |
| Status grid | docker available, state badge, service name, health, message |
| Actions | **Install**, **Start**, **Stop** → `POST /api/rag-providers/docker/{id}/{action}` |
| Action status line | `#rag-docker-action-status` |

Poll status every 10s while panel visible (same as actor runtime).

### 2.6 Terminal command panel

Reuse `terminal-command-panel.js`:

- `data-context-type="rag-provider"`
- `data-context-key="{providerId}"`

**Preset commands (dropdown):**

| Provider | Preset |
| --- | --- |
| LightRAG | `docker compose -f docker/rag-providers/docker-compose.yml pull lightrag` |
| LightRAG | `docker compose -f docker/rag-providers/docker-compose.yml up -d lightrag` |
| LightRAG | `docker compose -f docker/rag-providers/docker-compose.yml logs -f lightrag` |
| Cognee | `docker compose -f docker/rag-providers/docker-compose.yml pull cognee-mcp` |
| Cognee | `docker compose -f docker/rag-providers/docker-compose.yml up -d cognee-mcp` |

Editable command input + **Run** → `/api/terminal/run`.

### 2.7 Test retrieval panel

Collapsible “Test query” for operators:

- Text input: sample question
- Optional collection / dataset id
- **Run test** → `POST /api/rag-providers/query`
- Results: chunk list (score, source, truncated text)

## 3. Scenario flow integration (minimal v1)

On **Scenarios** page, `LlmNode` inspector `contextStrategy` dropdown already lists `rag` / `graph_rag`.

Add read-only hint under dropdown when strategy is RAG:

> Uses default provider: **LightRAG** ([configure](/Dashboard/RagProviders))

Optional v1.1: per-node `ragProviderId` override in inspector.

## 4. Empty / error states

| State | UX |
| --- | --- |
| Docker not installed | Red badge; link to Docker docs; terminal presets still copyable |
| Compose file missing | “Add `docker/rag-providers/docker-compose.yml` to repo” |
| Provider unhealthy | Amber on current card; test query shows error detail |
| No API key (Cognee) | Inline warning on cognify; query may still work if graph exists |

## 5. Accessibility & consistency

- Same Tailwind tokens as Actor runtime (`Default.cshtml` component structure).
- View component: `RagProvidersDashboardViewComponent` + `Pages/Dashboard/RagProviders.cshtml`.
- JS: `rag-providers-dashboard.js` (fork of `actor-runtime-dashboard.js` with route prefix changes).

## 6. Acceptance (UX)

1. Operator can select LightRAG, start Docker, run test query, see chunks — without editing JSON by hand.
2. Operator can switch to Markdown only and confirm Project Memory playground uses file-based context only.
3. Nav link highlights on `/Dashboard/RagProviders`.
4. Mobile: cards stack; Docker panel remains usable.
