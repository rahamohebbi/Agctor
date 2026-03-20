# PRD-008: Chat Trace UI and API Inventory

## Purpose

Define the Dashboard chat and Trace Timeline elements required to link historical prompts and responses to their stored trace timelines. This inventory is used to keep the backend contract and frontend behavior aligned before implementation starts.

## Scope

- **In-scope:** chat transcript rendering, prompt/response trace selection affordances, timeline loading states, and API metadata needed to support those interactions.
- **Out-of-scope:** a brand-new timeline visualization component, non-chat observability dashboards, and cross-session analytics views.

## Component Grouping Table

| UI or API element | Self-contained component or contract? |
| --- | --- |
| **Chat transcript shell** (session picker, new session, active session label, transcript container) | **No** - keep this as part of the existing `AgentChat` page-level workflow; it coordinates session loading and prompt sending. |
| **Historical turn row** (grouped request + response with stable turn id and selected state) | **Yes** - treat this as a distinct render boundary inside chat because turn-level trace selection is the default interaction model. |
| **Turn trace badge/button** (default trace affordance for one prompt/response pair) | **Yes** - isolate as a small reusable UI fragment so every trace-aware turn behaves consistently. |
| **Message trace badge/button** (request-level or response-level drill-down) | **Yes** - render only when message-level trace metadata differs from the primary turn trace. |
| **Trace availability indicator** (has trace, no trace, legacy session, backend unavailable) | **Yes** - keep this state small and reusable so transcript rows and timeline states stay consistent. |
| **Trace timeline panel** (existing timeline host and visualization) | **Yes** - keep the current `TraceTimeline` component boundary and feed it historical trace selections. |
| **Timeline selection summary** (which turn or message is currently selected) | **Yes** - add a focused summary row or label near the timeline so users know which historical item they are inspecting. |
| **Transcript trace metadata contract** (stable ids, grouping, trace ids, availability flags) | **Yes** - define this as an explicit API contract because the frontend can no longer rely on plain `role` + `content` alone. |
| **Direct trace lookup endpoint** (load by turn id or message id) | **Yes** - optional but useful as a dedicated integration contract when transcript metadata alone is insufficient. |

## Proposed Initial UI and API Set

- `HistoricalTurnRow`: grouped prompt/response rendering with selected state.
- `TurnTraceBadge`: default click target for loading the turn-level trace.
- `MessageTraceBadge`: optional request/response drill-down trigger.
- `TraceAvailabilityPill`: compact state indicator for trace availability.
- `TraceSelectionSummary`: label describing the currently loaded historical trace.
- `TraceTimeline`: keep the existing standalone timeline component.
- `TranscriptTraceMetadata`: API contract describing stable ids and trace handles.

## Interaction Rules

- Clicking a historical turn trace badge loads the canonical turn-level trace.
- Clicking a request or response trace badge loads the message-specific trace only when it is distinct from the turn-level trace.
- The selected turn or message remains highlighted while its trace is loaded.
- Sending a new live prompt should still auto-load the newest trace when available.
- Historical transcript rendering must work even when some older messages have no trace metadata.

## Required Transcript Metadata

Each transcript response should provide enough data to render trace-aware history without guessing:

- `sessionId`
- `turnId`
- `sequence`
- request message id
- response message id
- role
- content
- agent id
- primary turn trace id
- optional request trace id
- optional response trace id
- `hasTrace`
- optional trace status hint for legacy or unavailable data

## Trace Timeline States

- `Idle` - no historical trace selected yet.
- `Loading` - trace lookup or timeline fetch is in progress.
- `Ready` - timeline rendered successfully.
- `NoTrace` - the selected message or turn has no stored trace metadata.
- `NotFound` - metadata exists but the trace backend did not return a timeline.
- `Unavailable` - the trace backend or retrieval service could not be reached.

## Notes

- Keep turn-level selection as the primary user experience.
- Keep message-level drill-down hidden unless it adds real value.
- Reuse the existing timeline panel instead of creating a second debugging surface.
- Prefer compact trace affordances so chat readability stays high.
