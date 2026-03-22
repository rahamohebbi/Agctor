# PRD-009 — Trace Timeline Experience Improvements

**Folder status:** **Closed (specification).** Requirements and plans are frozen here; execution of the **enhancement backlog** is **not started** and should be scheduled as a future milestone.

## Documents

| File | Purpose |
| --- | --- |
| [prd-009-trace-timeline-improvements.md](./prd-009-trace-timeline-improvements.md) | Full PRD: goals, requirements, architecture, acceptance criteria |
| [prd-009-implementation-plan.md](./prd-009-implementation-plan.md) | Phased delivery plan (unchanged; use when work is prioritized) |
| [prd-009-implementation-status.md](./prd-009-implementation-status.md) | **What shipped vs backlog** — source of truth for progress |
| [prd-009-trace-timeline-ui-elements.md](./prd-009-trace-timeline-ui-elements.md) | UI/API inventory for frontend/backend alignment |

## Relationship to PRD-008

- **PRD-008** delivered historical chat ↔ trace linking, durable timeline snapshots, and dashboard loading of a trace from a turn/message.
- **PRD-009** describes **the next UX layer** on that timeline (search, filters, error navigation, virtualization, richer DTOs, etc.). The current product already includes a **baseline** timeline (see implementation status); it does **not** yet implement most PRD-009 bullets.

## When reopening this work

1. Start from **implementation-status** backlog rows.
2. Run **Phase 1** of the implementation plan (DTO audit against live `TraceTimelineResponse` / `TraceTimelineEventDto` and `TraceTimeline/Default.cshtml`).
3. Follow workspace rules: build solution, unit tests, then integration tests when shipping features.
