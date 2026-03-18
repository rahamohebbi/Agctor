# PRD-006: Implementation Plan — Session Memory and Persistence for Agent Chat

This document breaks down the implementation of [PRD-006 (Session Memory and Persistence)](./prd-006-session.md) into ordered, actionable steps. Work is placed only under existing assemblies: **AgctorSDK.Core**, **AgctorSDK.Agents**, **AgctorSDK.Host**, **AgctorSDK.Core.Tests**, **AgctorSDK.Core.IntegrationTests**, and **AgctorSDK.Host.IntegrationTests**.

## Implementation Status

- **Overall status:** Completed.
- **Execution status:** All plan phases 1-7 completed.
- **Post-plan hardening completed:** Query/Refactor/Coder behavior aligned to the same robust session-follow-up pattern (indexed behavior first, session-aware fallback, deterministic guardrails).

---

## Phase 1: Core contracts and persistence abstraction

**Goal:** Define canonical session models and interfaces without coupling to storage/runtime implementation details.

### 1.1 Session domain models (Core)

| Step | Action | Location |
|------|--------|----------|
| 1.1.1 | Add session DTOs: `SessionInfo`, `SessionTurn`, `SessionTranscript`, `SessionContextPackage`, and summary DTO (short meaningful names). | `AgctorSDK.Core/*` (new folder such as `Sessions/Models`) |
| 1.1.2 | Add actor message contracts for session operations: create/load/list/append/context retrieval. | `AgctorSDK.Core/*` (new folder such as `Sessions/Messages`) |
| 1.1.3 | Add small constants/options model for memory policy (window size, summary cadence, max context chars/tokens). | `AgctorSDK.Core/*` |

### 1.2 Interfaces (Core)

| Step | Action | Location |
|------|--------|----------|
| 1.2.1 | Define `ISessionStore` for durable metadata + turn append/read operations. | `AgctorSDK.Core/Interfaces/ISessionStore.cs` |
| 1.2.2 | Define `ISessionContextComposer` for deterministic context packaging from transcript + current request. | `AgctorSDK.Core/Interfaces/ISessionContextComposer.cs` |

### 1.3 Verification

- Add unit tests for model invariants (ordering, required fields, serialization safety).
- Add unit tests for context composition policy with deterministic truncation.
- **Status:** Completed.

---

## Phase 2: Session agents (Actor Model)

**Goal:** Implement actor-owned session memory with strict session isolation.

### 2.1 SessionMemoryAgent

| Step | Action | Location |
|------|--------|----------|
| 2.1.1 | Implement `SessionMemoryAgent` with per-session in-memory working state plus store-backed load/save. | `AgctorSDK.Agents/Agents/SessionMemoryAgent.cs` |
| 2.1.2 | Handle messages: append turn, get recent turns, get context package, get transcript snapshot. | Same file |
| 2.1.3 | Add concise comments explaining session ownership/isolation guarantees. | Same file |

### 2.2 SessionCoordinatorAgent

| Step | Action | Location |
|------|--------|----------|
| 2.2.1 | Implement `SessionCoordinatorAgent` to create/resolve session actors by `sessionId`. | `AgctorSDK.Agents/Agents/SessionCoordinatorAgent.cs` |
| 2.2.2 | Manage actor lifecycle and route operations to proper `SessionMemoryAgent`. | Same file |
| 2.2.3 | Enforce guardrails: reject cross-session retrieval requests by contract shape. | Same file |

### 2.3 Verification

- Unit tests: coordinator routing, session actor creation, and session isolation.
- Concurrency test: parallel append/read across different sessions remains independent.
- **Status:** Completed.

---

## Phase 3: Host storage implementation and DI

**Goal:** Provide durable persistence and wire everything in Host startup.

### 3.1 Durable store implementation

| Step | Action | Location |
|------|--------|----------|
| 3.1.1 | Implement `ISessionStore` in Host (recommended SQLite-backed implementation). | `AgctorSDK.Host/Services/SessionStore/SqliteSessionStore.cs` |
| 3.1.2 | Add schema bootstrap/migration-on-start for session metadata + session turns. | Same folder |
| 3.1.3 | Add read/list APIs with pagination support and predictable ordering. | Same folder |

### 3.2 DI and runtime registration

| Step | Action | Location |
|------|--------|----------|
| 3.2.1 | Register `ISessionStore` and `ISessionContextComposer` in DI container. | `AgctorSDK.Host/Program.cs` |
| 3.2.2 | Register/spawn session coordinator actor for the active runtime scenario where chat is enabled. | `AgctorSDK.Host/Services/Scenarios/CodeGraphDemoScenario.cs` (and/or scenario setup path) |

### 3.3 Verification

- Host integration test: store writes and reads are durable across process restart boundary (or simulated reinitialization).
- **Status:** Completed.

---

## Phase 4: Session API endpoints

**Goal:** Expose session lifecycle and session-aware messaging over HTTP.

### 4.1 Chat session controller

| Step | Action | Location |
|------|--------|----------|
| 4.1.1 | Add endpoint `POST /api/chat/sessions` (create). | `AgctorSDK.Host/Controllers/ChatSessionsController.cs` |
| 4.1.2 | Add endpoint `GET /api/chat/sessions` (list with lightweight metadata). | Same file |
| 4.1.3 | Add endpoint `GET /api/chat/sessions/{id}` (load session details and transcript). | Same file |

### 4.2 Session-aware message path

| Step | Action | Location |
|------|--------|----------|
| 4.2.1 | Extend message request model to carry `sessionId` in metadata/header-friendly form. | `AgctorSDK.Host/Models/ApiModels.cs` |
| 4.2.2 | Propagate `sessionId` through `MessageDispatcher` envelope creation and runtime headers/metadata. | `AgctorSDK.Host/Services/MessageDispatcher.cs` |
| 4.2.3 | Append user turn before dispatch and assistant turn after response via coordinator route (for chat-only paths). | Host service/controller integration points |

### 4.3 Verification

- API integration tests for create/list/load.
- API integration test for message send with `sessionId` writes both user + assistant turns.
- **Status:** Completed.

---

## Phase 5: Agent prompt integration

**Goal:** Ensure LLM-facing flows use same-session history context.

### 5.1 Refactor-agent integration first

| Step | Action | Location |
|------|--------|----------|
| 5.1.1 | Before LLM call, request session context package from coordinator using `sessionId`. | `AgctorSDK.CodeGraph/Agents/RefactorAgent.cs` |
| 5.1.2 | Compose LLM prompt with context package + current request in a deterministic template. | Same file |
| 5.1.3 | Preserve existing code-search context; merge with session context cleanly. | Same file |

### 5.2 Optional extension

| Step | Action | Location |
|------|--------|----------|
| 5.2.1 | Add same pattern for other chat-capable LLM orchestration paths if needed. | Relevant agent files in `AgctorSDK.CodeGraph` / `AgctorSDK.Agents` |

### 5.3 Verification

- Integration test: ambiguous follow-up (`"add it to MathUtils"`) succeeds in same session after prior turn.
- Integration test: same follow-up in a fresh session remains ambiguous (expected behavior).
- **Status:** Completed, then expanded with additional agent hardening patterns.

---

## Phase 6: Dashboard session UX

**Goal:** Make sessions visible and controllable from chat UI.

### 6.1 CodeGraph dashboard updates

| Step | Action | Location |
|------|--------|----------|
| 6.1.1 | Add session selector and “new session” button in chat section. | `AgctorSDK.Host/Pages/Dashboard/CodeGraph.cshtml` |
| 6.1.2 | Load sessions list and active session transcript from new chat session endpoints. | Same file (JS section) |
| 6.1.3 | Include active `sessionId` when sending prompt to agent message endpoint. | Same file (JS send handler) |

### 6.2 UX behavior

| Step | Action | Location |
|------|--------|----------|
| 6.2.1 | On session switch, clear/render message list from selected session transcript. | Same page/script |
| 6.2.2 | On send, append optimistic user turn and reconcile with server response. | Same page/script |

### 6.3 Verification

- Manual test: create two sessions, send different context, verify no cross-session bleed.
- Manual test: reload page, reopen old session, history appears.
- **Status:** Completed, with additional UI hardening for transcript rendering and actor-tree file preview rebinding.

---

## Phase 7: Hardening, docs, and tests

**Goal:** Close quality gaps and publish final behavior.

| Step | Action | Location |
|------|--------|----------|
| 7.1 | Harden LLM response parsing by normalizing fenced JSON before parse and surfacing explicit ambiguity errors. | `AgctorSDK.CodeGraph/Agents/RefactorAgent.cs` |
| 7.2 | Add trace/log enrichment with `sessionId` for diagnostics. | Host + agent logging points |
| 7.3 | Update docs/endpoints diagrams for session APIs and flow. | `AgctorSDK.Host/docs/*` and PRD docs |
| 7.4 | Add/finish unit + integration test suites for session lifecycle and isolation. | `AgctorSDK.Core.Tests`, `AgctorSDK.Core.IntegrationTests`, `AgctorSDK.Host.IntegrationTests` |

- **Status:** Completed.

---

## Post-Plan Enhancements (Implemented)

These are refinements applied after the base plan was completed, based on runtime behavior and user feedback:

| Area | Enhancement | Location |
|------|-------------|----------|
| Query behavior | LLM-first with deterministic backup for structured failures, preserving indexed-search-first behavior and session fallback search when primary context is empty. | `AgctorSDK.CodeGraph/Agents/QueryAgent.cs` |
| Refactor behavior | Applied the same pattern used in query flow: primary indexed behavior first, session-aware search fallback, stronger LLM failure detection, malformed JSON repair pass, and safe direct command path for prebuilt `CodeEditorTool` prompts. | `AgctorSDK.CodeGraph/Agents/RefactorAgent.cs` |
| Coder behavior | Deterministic guidance path for non-`CodeEditorTool` prompts so natural-language requests are routed to `refactor-agent` rather than failing with opaque tool errors. | `AgctorSDK.Agents/Agents/CoderAgent.cs` |
| Dashboard chat UX | Markdown-safe rendering with sanitizer/fallback parser, plus consistent transcript rendering and actor-tree file-node rebinding after refresh. | `AgctorSDK.Host/Pages/Dashboard/CodeGraph.cshtml` |
| Regression tests | Added dedicated tests for query follow-up, refactor fallback/repair behavior, coder routing guidance, and post-chat file-preview continuity. | `AgctorSDK.CodeGraph.Tests`, `AgctorSDK.Core.Tests`, `AgctorSDK.Host.IntegrationTests` |

---

## Dependency order

- Phase 1 is foundational.
- Phase 2 depends on Phase 1.
- Phase 3 depends on Phase 1 and partially on Phase 2 contracts.
- Phase 4 depends on Phase 2 and 3.
- Phase 5 depends on Phase 2 and 4.
- Phase 6 depends on Phase 4.
- Phase 7 runs throughout but finalizes after 5 and 6.

## Suggested implementation order

1. Phase 1 (Core contracts + interfaces)
2. Phase 3.1 (store implementation) in parallel with Phase 2 (session agents)
3. Phase 4 (session APIs and message path integration)
4. Phase 5 (refactor-agent context integration)
5. Phase 6 (dashboard UX)
6. Phase 7 (hardening, docs, and tests)

This order delivers a minimal usable flow early (session create -> chat with memory -> reload) while preserving clean assembly boundaries and Actor Model encapsulation.
