# PRD-016: Scenario persona persistence and playground trace debugging

## 1. Overview

Operators testing **people** scenarios in **Project memory → Playground** need two things:

1. **Persistence:** When the user asks in natural language to **persist, save, or write** what the chat already knows about a person **into markdown (and related persona files)** on disk, the system should do so **reliably**, not only when the model happens to emit the right JSON in the right step.
2. **Trace debugging:** The **Trace timeline** on the same page should make each span’s **inputs** and **outputs or outcomes** easy to scan, with **visually separated sections**, so debugging Router → PersonaCall chains does not require mentally parsing one long block of text.

This PRD describes product intent, constraints of the current architecture, and acceptance criteria. Implementation sequencing lives in [prd-016-implementation-plan.md](./prd-016-implementation-plan.md).

## 2. Goals

### 2.1 Scenario-aligned “save to disk”

1. For scenario runs where the catalog flow includes **person-extractor** (or a successor agent with the same **memoryIntents JSON** contract), user requests such as *“persist what we know”*, *“save this to their profile”*, or *“write that to markdown”* should **consistently** result in **disk updates** when the conversation already contains extractable facts, subject to guardrails (no invention of facts).
2. **Scenario-scoped paths** remain authoritative: persona files for the run live under project-relative `scenarios/<scenario>/people/` (as already documented in prompts and playground copy), not only under project-root `people/` unless the product explicitly defines that bridge.
3. Operators should be able to **infer from the UI** why a turn **did or did not** write files (skipped ingest, parse failure, wrong agent, missing scenario id, etc.) without reading server logs.

### 2.2 Trace timeline drill-down

1. For each playground-related span that carries a `timelineDetailJson` payload, the details panel should present at minimum:
   - **Input** — what went into the step (e.g. full LLM prompt, or structured summary for non-LLM steps if raw input is huge).
   - **Output or outcome** — what came out (model text, parse result, list of paths written, errors), **in a separate block** from input, with consistent headings and spacing.
2. The **list rows** above the chart may continue to show timing and parent ids; optionally, each row can surface a **one-line outcome** (e.g. “wrote 3 files”, “parse failed”) when data is available.
3. Changes should apply to the **shared** `TraceTimeline` view component so **CodeGraph** and **Playground** both benefit unless a page explicitly opts out.

## 3. Non-goals

1. Replacing the **actor-model** execution model with a synchronous CRUD API; persistence remains **ingest-driven** from structured extractor output unless a later PRD introduces a dedicated tool actor for writes.
2. Full **PRD-009** backlog (search, virtualization, global filters) in this milestone.
3. Authenticated multi-tenant safety for Host dashboards (local dev assumption unchanged).

## 4. Current behavior (baseline)

The following is the **intended** architecture today; PRD-016 closes gaps between this and operator expectations.

- **Disk writes in playground** occur when **person-extractor** output parses as **`memoryIntents` JSON** and **ingest** succeeds. Later personas (e.g. memory-curator) are **narrative** in the stream unless the prompt already includes ingest results; they do not call `write_document` in this path.
- **Ingest** is tied to **agent id** `person-extractor` and a **resolved scenario id** (`ingestActive` in `ProjectMemoryController`). If routing sends a “save” user utterance only to a **non-extractor** persona, **no markdown update** occurs even if the assistant **says** it saved something.
- **Trace payloads** for `pm.playground.persona-llm` already include both `prompt` and `output` in JSON (`PlaygroundTraceTimelineDetail.BuildPersonaLlmJson`). The UI may still feel “lumped” if sections are not visually strong enough, if **output is empty** for failed parses, or if other span kinds lack a symmetric **input** field.

## 5. Requirements

### 5.1 Persistence and scenarios

| ID | Requirement |
| --- | --- |
| P1 | **Routing:** For scenarios whose goal includes maintaining markdown persona files, **routing rules** (or Router LLM instructions) must treat utterances that express **commit / save / persist** intent as **high priority** for the **person-extractor** step when prior turns contain factual content to extract. |
| P2 | **Agent instructions:** **person-extractor** (and shared templates) should explicitly describe that **user ask to save** still requires emitting **`memoryIntents`** for facts already stated in the thread (no new invention). |
| P3 | **Flow design:** Default or sample flows that include **two PersonaCall** steps should document which step is responsible for **structured extraction** vs **narration/curation**, so “save” prompts are not answered only by the curator. |
| P4 | **Honest assistant text:** If ingest is skipped or fails, streamed assistant copy (from any persona) must not **imply** successful disk writes; optional server-side **footer** or trace-linked summary may clarify actual write status (align with existing ingest footer patterns if present). |
| P5 | **Playground UX:** The **Request pipeline** card copy should briefly state that **natural-language save** only affects disk when **extractor JSON ingest** runs successfully, with a link or tooltip to **Trace timeline** spans `ingest-disk` / `persist-assistant`. |

### 5.2 Trace timeline

| ID | Requirement |
| --- | --- |
| T1 | **Visual structure:** For `pm.playground.persona-llm`, render **Input** and **Output or outcome** in **distinct** panels (e.g. bordered cards, labels, optional collapse/expand for long text). |
| T2 | **`pm.playground.ingest-disk`:** Add or surface **input** summary where useful (e.g. truncated raw JSON or hash + char count) and **outcome** (parse success, `wroteAnyFile`, summary, paths) in **two** sections. If full JSON is too large, show **metrics + first N chars** and “truncated” flag. |
| T3 | **`pm.playground.persist-assistant`:** Separate **session/message identifiers** from **outcome** (chars written, ingest summary, per-file read status) and from **artifact preview** (file contents). |
| T4 | **`http.project-memory.playground-stream` (optional stretch):** When feasible, attach a **lightweight** `timelineDetailJson` (session id, scenario id, agent chain summary, final status) so the root span is not always “No drill-down payload.” |

## 6. Acceptance criteria

1. **Save prompt, happy path:** Given a playground session with a **resolved scenario** and a flow that reaches **person-extractor** with prior user messages stating facts, a user message *“Please persist everything we know about this person to markdown.”* yields **ingest** writing at least one expected file under `scenarios/<id>/people/...` when the model emits valid `memoryIntents` JSON (same as today), and the **Trace** row for **`pm.playground.ingest-disk`** shows **wrote files: true** with listed paths.
2. **Save prompt, routing failure:** If the same utterance is routed to a **non-extractor** persona only, the UI or transcript makes it **obvious** that **disk ingest did not run** (flow chip skipped or trace shows no successful ingest), and the assistant does not claim successful file writes.
3. **Trace readability:** Opening drill-down for **`pm.playground.persona-llm`** shows **Input** and **Output or outcome** sections without scrolling past an undifferentiated wall of text (minimum: clear headings + separation; preferred: card layout).
4. **Ingest / persist spans:** Drill-down for **`pm.playground.ingest-disk`** and **`pm.playground.persist-assistant`** uses the same **Input / Outcome** pattern as far as data allows.
5. **Tests:** New or updated **unit** tests for any new trace DTO shaping or ingest metadata; **integration** tests for playground stream or visualization timeline if behavior changes on the wire.

## 7. Security and data

- Playground continues to operate on **operator-selected project roots**; no new exfiltration surface beyond existing file read previews in trace details.
- Trace JSON remains **size-capped**; truncation flags must remain visible.

## 8. Open questions

1. Should **explicit user confirmation** (“Type SAVE to write”) be a configurable guardrail for destructive overwrites?
2. Should **curator** receive **structured ingest results** automatically on every turn after extractor, or only when extractor ran in that turn?
