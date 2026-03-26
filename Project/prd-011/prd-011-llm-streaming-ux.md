# PRD-011: LLM streaming and actionable error UX

## Goals

1. Stream **Ollama** token output to the CodeGraph dashboard while refactor/query (and any path hitting `LLMAgent`) runs, so users see progress instead of a long silent wait.
2. Present **errors and outcomes** in a structured way: clear severity, copy-friendly text, trace linkage, and hints for next steps (e.g. index code, retry, open Agents).

## Non-goals (v1)

- Persisting partial LLM text to `ISessionStore` before the turn completes.
- Bi-directional WebSocket chat or server-initiated push outside an active streaming request.
- Streaming from non-Ollama providers without a follow-up PR.

## User stories

- As a developer using CodeGraph chat, I want to **see LLM text appear incrementally** so I know the system is working and can skim early output.
- As a developer, I want that incremental text to **look like the final reply** (markdown lists, bold, code spans) **while it streams**, not only after the turn is saved.
- As a developer, I want **phase labels** (e.g. dispatch, LLM planning) when available so I understand where time is spent.
- As a developer, when something fails, I want **actionable UI** (copy error, open trace, retry) without digging raw JSON.

## Dashboard UX (as implemented)

### In-transcript streaming (no separate panel)

1. User sends a message → **You** line is appended immediately.
2. A **pending assistant bubble** is appended below it: same **green** styling as assistant turns from `loadSessionTranscript` (border/background classes aligned with transcript bubbles).
3. Bubble header: **agent id** (e.g. `query-agent`), **phase** text (from SSE `phase` events), **Copy** (copies rendered text via `innerText`).
4. Bubble body: `<span class="codegraph-chat-markdown">` updated with **`renderChatMarkdown(accumulatedText)`** on each frame (`requestAnimationFrame` coalescing + final flush) so live output matches **marked** + **DOMPurify** (or **renderBasicMarkdown** fallback) used for historical turns.
5. When the request completes, **`loadSessionTranscript`** rebuilds the thread (turn groups, trace chips, markdown for all turns). The pending bubble is replaced by server-backed layout.

### Coding agents

- **coder-agent** / **refactor-agent** use the same in-transcript stream for LLM-visible phases.
- **Completion banner** below the messages area: success vs issues, **Copy summary**, **Show trace** (when `traceId`), link to **Agents**; existing re-index / actor-tree refresh behavior preserved.

### Fallback

- If `POST .../message/stream` is not SSE or fails, the pending bubble is **removed** and **`POST .../message`** is used; transcript refresh applies as today.

## Technical approach

- **Opt-in endpoint:** `POST /api/agents/{agentId}/message/stream` returns `text/event-stream` (SSE). Same body as `message`.
- **Correlation:** Server generates a `streamId`, registers a channel, adds header **`agctor-stream-id`** to the actor envelope. `LLMAgent` uses Ollama `stream: true`, parses NDJSON lines, publishes **`llm_delta`** / **`llm_done`** events to the registry keyed by `streamId`.
- **Nested calls:** `RefactorAgent` and `QueryAgent` merge `agctor-stream-id` and `trace-id` into outbound LLM requests.

## SSE event schema (JSON in each `data:` line)

| `type` | `payload` | Notes |
| --- | --- | --- |
| `phase` | string | Human-readable step |
| `llm_delta` | string | Token or fragment from Ollama |
| `llm_done` | optional string | Full concatenated response (optional; client may already have deltas) |
| `error` | string | Recoverable or terminal error message |
| `done` | JSON object | Final result: `status`, `responseData`, `traceId`, `errorMessage` (mirrors `MessageResponse` fields) |

## Acceptance criteria

1. CodeGraph send uses streaming when the stream endpoint returns SSE; falls back to non-streaming POST on failure.
2. With Ollama available, the UI shows **live markdown-aware** text during LLM generation inside the assistant bubble.
3. `POST .../message` unchanged and still used by fallback and non-UI clients.
4. Final session transcript still contains one assistant turn per completed request.
5. Error banner includes copy (and trace focus when `traceId` is present).
6. Streaming UI is **integrated with the turn transcript** (no duplicate “live” panel above messages).

## Future (phase 2)

- `step` index on `llm_delta` when multiple LLM calls occur in one request.
- Optional cancellation via client disconnect propagated to Ollama.
- Optional debounce/tuning for markdown re-parse cost on very fast token rates (beyond per-frame coalescing).
