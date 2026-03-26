# PRD-011 — LLM streaming and actionable error UX

**Folder status:** Active — implementation tracks this specification.

## Documents

| File | Purpose |
| --- | --- |
| [prd-011-llm-streaming-ux.md](./prd-011-llm-streaming-ux.md) | Full PRD: goals, event schema, UX, acceptance criteria |
| [prd-011-implementation-plan.md](./prd-011-implementation-plan.md) | Phased delivery plan (delivered + UI refinements) |

## Relationship to PRD-010

- **PRD-010** delivered the Agents dashboard, CodeGraph demo workspace, and compile gate. **PRD-011** adds **opt-in** SSE streaming from the Host while agents run (Ollama token deltas + phases) and improves CodeGraph chat UX for errors and next steps, without removing the existing `POST /api/agents/{id}/message` API.

## Implemented UX summary

- Streaming appears **inside the chat transcript**: after **You**, a **green assistant-style bubble** (same visual language as saved turns) shows the selected **agent id**, **phase** subtitle, **Copy**, and **live markdown** (`renderChatMarkdown` — `marked` + `DOMPurify` when loaded on the CodeGraph page, else basic markdown). Updates are **coalesced per animation frame**; a final flush runs when the SSE stream ends. **`loadSessionTranscript`** then replaces the list with server truth (turn groups + trace buttons).
- **coder-agent / refactor-agent**: same in-transcript stream; **completion banner** (copy summary, show trace, Agents link) and post-run behaviors (trace, re-index) unchanged.
- There is **no separate “live stream” panel** in the Agent chat component.

## When changing behavior

1. Keep non-streaming message API stable for MCP and scripts.
2. Propagate header `agctor-stream-id` on any internal `SendMessageAsync` to `llm-agent` so nested LLM calls stream to the same client session.
3. Session transcript still stores the **final** assistant turn after completion (partial streaming text is not persisted in v1).
4. Keep live streaming markdown aligned with **`renderChatMarkdown`** / **`.codegraph-chat-markdown`** so streaming matches post-load rendering.

## Key code locations

| Area | Location |
| --- | --- |
| SSE + registry + LLM stream | `AgentsController`, `MessageDispatcher`, `LLMAgent`, `AgentOutputStreamRegistry`, `Program.cs` |
| Header merge | `RefactorAgent`, `QueryAgent` |
| Chat UI | `wwwroot/js/dashboard/codegraph-page.js` (`createStreamingReplyBubble`, `tryAgentMessageStream`, `renderChatMarkdown`) |
| Chat chrome | `Pages/Shared/Components/AgentChat/Default.cshtml` |
