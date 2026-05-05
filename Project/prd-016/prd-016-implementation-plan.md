# PRD-016: Implementation plan — Persona persistence + trace sections

**Status:** Planned — execute phases in order; each phase should leave the product shippable.

Follow workspace rules: after a major slice, **build all projects**, then **unit tests**, then **integration tests**.

## Phase 1 — Trace timeline UX (quick win)

**Objective:** Make drill-down **unmistakably** structured for existing payloads.

1. **`TraceTimeline/Default.cshtml`** — Refactor `formatTimelineDetailJson` HTML for:
   - `pm.playground.persona-llm`: bordered **Input** / **Output or outcome** cards; keep truncation warnings visible.
   - `pm.playground.ingest-disk`: add **Input** block (char counts + optional truncated extractor snippet if the backend adds a field; if not in Phase 1, show explicit “Input: not captured in trace” to reserve layout) and **Outcome** block (parse, wrote, summary, paths).
   - `pm.playground.persist-assistant`: **Identifiers** vs **Outcome** vs **File previews** as three visual groups.
2. **Optional backend (same phase if small):** Extend `PlaygroundTraceTimelineDetail.BuildIngestJson` with `extractorOutputChars` and `extractorOutputPreview` (truncated) so **T2** is satisfied without giant payloads.
3. **Manual verify:** Playground → send a message → open each span type and confirm layout on light and dark theme.

**Exit:** PRD §6 items **3** and **4** satisfied for UI; **T1–T3** satisfied or explicitly deferred only for missing data fields (then Phase 2 adds fields).

## Phase 2 — Trace root span context (stretch)

**Objective:** Reduce “No drill-down payload” noise for `http.project-memory.playground-stream`.

1. **`ProjectMemoryController`** — When starting `http.project-memory.playground-stream`, set `SetTimelineDetailJson` with a compact JSON object: scenario id, session id, selected agent id, flow step count, terminal status.
2. **`formatTimelineDetailJson`** — Add a `default` or `kind`-specific branch for the new shape.
3. **Tests:** Assert timeline API returns the new field for a playground request (integration or controller-level test as appropriate).

**Exit:** PRD **T4** satisfied.

## Phase 3 — Persistence reliability (routing + copy + instructions)

**Objective:** Align **operator intent** (“save to markdown”) with **extractor + ingest** actually running.

1. **Sample + template YAML** — Update `person-extractor` instructions (and any Host template duplicates) for **P2**: save/persist phrasing must still output `memoryIntents` from **already stated** facts.
2. **Scenario routing** — In `samples/people-project` (and schema docs), adjust **routing-rules** / Router prompts so **P1**: save-like utterances prefer routing to **person-extractor** when the transcript contains extractable content.
3. **Flow documentation** — For the sample scenario attached to the people project, document in `.agctor` or scenario metadata which **LlmNode** is extractor vs curator (**P3**).
4. **Playground.cshtml** — Tighten **P5** copy to match actual behavior after routing changes.
5. **Honesty** — Audit streamed text paths in `ProjectMemoryController` / persona runner for **P4**; add a short system or post-process hint when ingest skipped.

**Exit:** PRD §6 items **1** and **2** satisfied on the sample scenario; document any remaining limitations in the readme “Open questions” if unresolved.

## Phase 4 — Hardening and regression

1. **AgctorSDK.Core.Tests** — Any new pure functions (trace JSON builders, routing helpers).
2. **AgctorSDK.Host.IntegrationTests** — Playground stream or visualization timeline assertions if Phase 2 ships.
3. Cross-link **PRD-009 implementation-status** if overlapping items are completed by this work.

## Risks

| Risk | Mitigation |
| --- | --- |
| Larger trace payloads slow the UI | Keep strict caps; previews only; collapse by default for large bodies. |
| Router over-steers to extractor | Tune rules; add tests with ambiguous utterances. |
| Models still emit prose instead of JSON | Reinforce JSON-only in instructions; consider repair pass (future PRD). |

## Module placement (reminder)

| Area | Project / path |
| --- | --- |
| Trace UI | `AgctorSDK.Host/Pages/Shared/Components/TraceTimeline/Default.cshtml` |
| Trace JSON | `AgctorSDK.Host/Services/ProjectMemory/PlaygroundTraceTimelineDetail.cs` |
| Playground orchestration | `AgctorSDK.Host/Controllers/ProjectMemoryController.cs` |
| Core ingest / orchestration | `AgctorSDK.Core` (as existing patterns dictate) |
| Sample agents / routing | `samples/people-project/.agctor/…` |
