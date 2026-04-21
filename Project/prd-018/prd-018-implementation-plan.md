# PRD-018: Implementation plan — Cross-session entity resolution

**Status:** Delivered — all six phases landed. Signal weights, thresholds, and the ingest sink
stay configurable per project via `.agctor/resolution.yaml`; the subsystem is enabled by default
on the sample project.

## Delivery checklist

| Area | Artifacts |
| --- | --- |
| Phase 1 — foundations | `AgctorSDK.Core/ProjectMemory/Resolution/{Models,Messages,Persistence,Policy}` |
| Phase 2 — actors | `Actors/{ResolutionSupervisorActor,ReconcilerActor,MentionIndexActor,ResolutionActor}` and the v1 signal producers |
| Phase 3 — cross-session | `SessionSummaryStore`, `SessionSummary` flow, `AttributeOverlap`, `GraphConsistency`, `EmbeddingSimilarity`, `ReloadPolicy` watcher |
| Phase 4 — promotion + ingest | `ResolutionActor` state machine, `SidecarIntentSink`, `MemoryIntentBridgeSink`, `CompositeResolutionIntentSink`, `ResolutionAnnotator` honest-narration footnotes |
| Phase 5 — UX surface | `ResolveSpanTraceSink`, `PlaygroundTraceTimelineDetail.BuildResolveJson`, `pm.playground.resolve` card in `TraceTimeline/Default.cshtml`, `ResolutionReviewController`, `Pages/Dashboard/ProjectMemory/ResolutionReview.cshtml`, pipeline-runner query annotation |
| Phase 6 — hardening | `ResolutionMetrics`, `ResolutionChaosIntegrationTests`, `.agctor/resolution.yaml` defaults-on on the sample, README refresh |
| DI + host wiring | `AgctorSDK.Core/DependencyInjection/ResolutionServiceExtensions.AddAgctorResolution`, `ResolutionBootstrapper`, `Program.cs` startup hook, `ChatSessionsController` session-close → `SessionSummaryEmitter` |

See `prd-018-readme.md` for the live module index; individual phase notes below remain the
reference for future changes.

Follow workspace rules: after a major slice, **build all projects**, then **unit tests**, then **integration tests**.

Feature gate: `resolution.enabled` in `.agctor/resolution.yaml` (default `false` until Phase 4) so partial work does not affect existing users.

## Phase 1 — Foundations: schema, disk layout, and mention observation

**Objective:** Make mentions a first-class event and give each entity a place to store incoming evidence, without any resolution logic yet.

1. **Models (core).**
   - `AgctorSDK.Core/ProjectMemory/Resolution/Models/ResolutionEdge.cs` — edge record matching PRD §5.5.
   - `ResolutionEdgeState` enum (`Soft`, `Hard`, `Rejected`, `Superseded`).
   - `ResolutionSignal`, `ResolutionPromotion`, `ResolutionProvenance` POCOs.
   - YAML (de)serialization via existing `ProjectYamlSerializer`.
2. **Disk sidecars.**
   - Reader/writer for `<entity>/.resolution/incoming.yaml` and `.resolution/promotions.log.yaml`.
   - Extend `EntityRegistry.DiscoverAsync` to *optionally* hydrate edges into a new `EntityRecord.Resolution` field (kept null when the folder is absent — zero impact on existing tests).
3. **Mention messages.**
   - `Resolution/Messages/MentionObserved.cs`, `SessionSummary.cs`.
   - Hook `person-extractor` ingest to emit `MentionObserved` for every entity reference produced (no consumer yet in Phase 1).
4. **Configuration.**
   - `.agctor/resolution.yaml` loader with defaults in `Resolution/Policy/ResolutionPolicy.cs`.

**Exit:** PRD §5.4 disk layout exists and is round-trippable; `MentionObserved` messages are visible in the event bus; feature flag off by default; all existing tests still pass.

## Phase 2 — Actor topology and reconciler (no promotion yet)

**Objective:** Stand up the three actors and the supervisor; produce *soft links only*, using a minimal signal set.

1. **Actors.**
   - `Resolution/Actors/ResolutionActor.cs` — per-entity state owner, appends evidence, publishes `EvidenceAppended`.
   - `Resolution/Actors/ReconcilerActor.cs` — consumes `MentionObserved` + `SessionSummary`, coalesces, dispatches `ResolveCandidate`.
   - `Resolution/Actors/MentionIndexActor.cs` — surface → candidate lookup projected from `EntityRegistry` + aliases.
   - `Resolution/Actors/ResolutionSupervisorActor.cs` — spawns the above via `IActorRuntimeAdapter`, rehydrates from disk on restart.
2. **Signal producers (v1).**
   - `Resolution/Signals/AliasMatcher.cs` (S1), `SurfaceUniqueness.cs` (S2), `InSessionCoref.cs` (S3), `NegativeAssertions.cs` (S7).
   - Each is a pure class with an async `ScoreAsync(candidate, mention, context)` returning `ResolutionSignal`.
3. **Reconciler logic.**
   - Coalesce by `(entityKey, mentionId)` within `coalesceWindowMs`.
   - Per-entity bounded queue; drop-oldest with warning on overflow.
   - Emit `ResolveCandidate` to each `ResolutionActor`; it runs the signal ensemble synchronously (cheap in v1) and writes/updates the edge.
4. **Supervision + persistence.**
   - On actor fault, supervisor respawns from disk-backed edge list; idempotency via `edgeId + inputsFingerprint`.
5. **Wiring.**
   - DI registration in `AgctorSDK.Extensions/DependencyInjection/`.
   - Startup: `ResolutionSupervisorActor` spawned per loaded project.

**Exit:** Acceptance criterion **1** (within-session soft link) passes on `samples/people-project`. Feature flag still off by default.

## Phase 3 — Cross-session reconciliation and richer signals

**Objective:** Reach Raha/Ryan cross-session case and improve confidence quality.

1. **Session summary emission.**
   - Hook `ProjectMemoryController` (or the session lifecycle service) to emit `SessionSummary` at session close / checkpoint. Persist to `sessions/<id>/summary.yaml`.
2. **Reconciler subscription.**
   - Reconciler consumes session summaries and re-runs signals across new mentions × registry on a throttled schedule.
3. **Additional signals.**
   - `AttributeOverlap.cs` (S4), `GraphConsistency.cs` (S6).
   - `EmbeddingSimilarity.cs` (S5) behind `resolution.embeddings.enabled`; no-op when disabled.
4. **Per-project policy reload.**
   - `ReloadPolicy` message re-weights pending edges on `.agctor/resolution.yaml` change (file watcher, debounce 1 s).

**Exit:** Acceptance criterion **2** (cross-session soft link) passes in integration test with two separate sessions.

## Phase 4 — Promotion, demotion, and ingest bridge

**Objective:** Close the loop so soft links can become hard links (and back) with full audit.

1. **State machine.**
   - Implement promotion rules P1–P5 in `ResolutionActor`.
   - `promotions.log.yaml` append-only writes with signal snapshots; atomic rename for crash safety.
2. **Ingest bridge.**
   - `Resolution/Bridge/IngestIntentDraftEmitter.cs` — translates `LinkStateChanged` into a `memoryIntents`-compatible delta, handed to the existing ingest runner.
   - Validation: intents are dry-run validated against the mention source file before being applied.
3. **Honest narration hooks.**
   - When the extractor persona claims a relationship, assistant post-process adds a “(soft-linked 72% → people/raha)” footnote pulled from the resolution store, mirroring PRD-016 §P4’s honesty constraint.
4. **Auto-promotion throttling.**
   - Promotion writes are serialized per entity to avoid rewriting the same mention site twice in one batch.

**Exit:** Acceptance criteria **3**, **4**, **5** pass. Feature flag still opt-in but documented as stable on sample project.

## Phase 5 — UI / trace surface and review workflow

**Objective:** Make resolution visible and reviewable without reading YAML.

1. **Trace span.**
   - `pm.playground.resolve` in `PlaygroundTraceTimelineDetail` with Input (candidate + registry slice), Evidence (signals table), Outcome (edge state + confidence).
   - Reuse the PRD-016 Input / Outcome card layout in `TraceTimeline/Default.cshtml`.
2. **Inline markers.**
   - Playground transcript renders soft links as `Raha ⟶ people/raha (72%)` with a popover linking to the evidence panel.
3. **Review tab.**
   - `Pages/Dashboard/ProjectMemory/ResolutionReview.cshtml` — list pending soft links with Confirm / Reject / Needs-more-evidence.
   - Controller endpoints `POST /api/project-memory/resolution/promote|demote|reject` that send the matching actor messages.
4. **Person-query integration.**
   - Query persona receives a resolved mention graph so answers reflect link grade (“likely Raha (72%)” vs “Raha”).

**Exit:** Acceptance criterion **6** (ambiguous no-op) visible in review tab; U1–U4 satisfied.

## Phase 6 — Hardening, defaults-on, and telemetry

1. **Observability.**
   - Per-project metrics: soft-link creation rate, promotion rate, rejection rate, mean confidence, reconciler queue depth.
   - Reuse `AgctorSDK.Core/Utils/Observability` patterns.
2. **Tests.**
   - Unit: each signal producer, promotion state machine, coalescer, policy loader.
   - Integration (core): reconciler end-to-end on `InMemory` runtime with two sessions.
   - Integration (host): trace span shape, review tab endpoints, idempotent restart.
   - Chaos: kill supervisor mid-batch; assert no duplicate evidence and no missing promotions.
3. **Flip the default.**
   - `resolution.enabled: true` by default once metrics on the sample project show stability for one week of dogfood runs.
4. **Docs.**
   - Update `AgctorSDK.Core/README.md` and each project’s `docs/class-diagram.mmd` to include resolution actors.
   - Generate JPEGs via `scripts/generate-images.sh` for the PRD-018 `docs/` Mermaid sources.

## Risks

| Risk | Mitigation |
| --- | --- |
| Signal noise produces too many low-quality soft links | Tune `softThreshold`; require at least 2 non-zero signals before surfacing in review tab. |
| Promotion races between reconciler and UI | `ResolutionActor` serializes writes; UI calls go through the same mailbox. |
| Embeddings provider flaky or unavailable | Signal is optional; absence is neutral, never a veto. |
| Huge evidence files over time | Rotate `.resolution/incoming.yaml` by size/age; cold evidence moves to `.resolution/archive/`. |
| Registry changes (entity renamed / deleted) orphan edges | On rename, supervisor rewrites `edgeId` and appends an `edgeRenamed` audit row; on delete, edges move to `superseded`. |
| Cross-scope leakage (scenario → project-root promotion without intent) | Scenario-scoped hard promotions require an explicit `scenarioGraduation` signal kind; otherwise they stay scenario-local. |
| Adapter portability regressions | Use only `IActorRuntimeAdapter` primitives; add adapter-agnostic integration test matrix. |

## Module placement (reminder)

| Area | Project / path |
| --- | --- |
| Resolution core (actors, models, policy, bridge, signals) | `AgctorSDK.Core/ProjectMemory/Resolution/` |
| Resolution unit tests | `AgctorSDK.Core.Tests/ProjectMemory/Resolution/` |
| Resolution integration tests | `AgctorSDK.Core.IntegrationTests/ProjectMemory/Resolution/` |
| Trace DTO extension | `AgctorSDK.Host/Services/ProjectMemory/PlaygroundTraceTimelineDetail.cs` |
| Trace timeline UI | `AgctorSDK.Host/Pages/Shared/Components/TraceTimeline/Default.cshtml` |
| Playground integration points | `AgctorSDK.Host/Controllers/ProjectMemoryController.cs` |
| Review tab + endpoints | `AgctorSDK.Host/Pages/Dashboard/ProjectMemory/`, `AgctorSDK.Host/Controllers/` |
| Sample policy + schema | `samples/people-project/.agctor/resolution.yaml` |
