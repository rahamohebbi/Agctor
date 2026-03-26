# PRD-011: Implementation plan — LLM streaming and actionable error UX

**Status:** Core phases 1–5 delivered. UI refinements: unified transcript bubble + live markdown (see PRD body).

## Phase 1: PRD and contracts

| Step | Action | Location |
| --- | --- | --- |
| 1.1 | Add `Project/prd-011/` readme + PRD + this plan | `Project/prd-011/` |
| 1.2 | Define `AgentStreamEvent` DTO and `agctor-stream-id` header name | `AgctorSDK.Core/Streaming/` |
| 1.3 | Define `IAgentOutputStreamRegistry` + in-Host registry; `AgentOutputStreamHub` for LLMAgent access | Core interface + Hub; `AgctorSDK.Host/Services/` |

## Phase 2: LLM and agents

| Step | Action | Location |
| --- | --- | --- |
| 2.1 | Ollama NDJSON streaming when `agctor-stream-id` present; `OllamaStreamLineParser` | `AgctorSDK.Core/Streaming/OllamaStreamLineParser.cs`, `LLMAgent.cs` |
| 2.2 | `MessageDispatcher`: optional `streamId`, header, initial `phase` event | `MessageDispatcher.cs` |
| 2.3 | Merge stream + trace headers on LLM calls | `RefactorAgent.cs`, `QueryAgent.cs` |
| 2.4 | `RefactorAgent` phase events (context / LLM / apply) | `RefactorAgent.cs` |

## Phase 3: HTTP SSE

| Step | Action | Location |
| --- | --- | --- |
| 3.1 | `POST .../message/stream`, SSE multiplex + final `done` | `AgentsController.cs` |
| 3.2 | Register DI + `AgentOutputStreamHub.Instance` at startup | `Program.cs` |

## Phase 4: UI

| Step | Action | Location |
| --- | --- | --- |
| 4.1 | Fetch streaming POST, parse SSE; **in-transcript** green assistant bubble (`createStreamingReplyBubble`) | `wwwroot/js/dashboard/codegraph-page.js` |
| 4.2 | **Live markdown:** `renderChatMarkdown` on accumulated text; `requestAnimationFrame` coalescing + flush on stream end | `codegraph-page.js` |
| 4.3 | Copy stream / copy summary; banner actions (trace, Agents) | `codegraph-page.js` |
| 4.4 | Agent chat markup: messages + status + banner only (no separate live panel) | `Pages/Shared/Components/AgentChat/Default.cshtml` |
| 4.5 | Markdown libs for parity with transcript | `Pages/Dashboard/CodeGraph.cshtml` (marked, DOMPurify — pre-existing) |

## Phase 5: Quality and docs

| Step | Action | Location |
| --- | --- | --- |
| 5.1 | Unit tests: `OllamaStreamLineParser` | `AgctorSDK.Core.Tests/Streaming/` |
| 5.2 | Integration test: SSE `text/event-stream` + `phase` + `done` | `AgentsMessageStreamIntegrationTests.cs` |
| 5.3 | Endpoints + class diagrams | `AgctorSDK.Host/docs/` |

## Dependency order

1 → 2 → 3 → 4 → 5; UI refinements (4.2–4.4) follow initial 4.1 delivery.
