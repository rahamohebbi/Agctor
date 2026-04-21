# Resolution subsystem (PRD-018)

Cross-session entity resolution that links mentions in documents or turns to canonical entity
folders, with graded confidence (soft / hard) and an append-only evidence trail. Built on the
actor model so per-entity state is isolated and location-transparent across the runtime adapters
AGCTOR already supports.

## Actors

| Actor | Scope | Responsibility |
| --- | --- | --- |
| `ResolutionSupervisorActor` | per project | spawns children from the entity registry and rehydrates them on restart |
| `ReconcilerActor` | per project | consumes `MentionObserved` / `SessionSummary`, coalesces work, dispatches `ResolveCandidate` |
| `MentionIndexActor` | per project | pure projection: surface form → candidate entities |
| `ResolutionActor` | per entity | only writer of `<entity>/.resolution/*.yaml`; runs signals, auto-promotes, emits ingest intents |

Actor addressing follows `DefaultResolutionAddressing` (`res:<proj>:<key>`, `rec:<proj>`, ...).

## Signals

| Kind | Producer | Notes |
| --- | --- | --- |
| `aliasMatch` | `AliasMatcher` | exact / substring compare against `DisplayName` + aliases |
| `uniqueness` | `SurfaceUniqueness` | 1 / matching entities for the normalized surface |
| `corefInSession` | (pending — wired through signal context) | in-session coref head resolution |
| `attrOverlap` | `AttributeOverlap` | token Jaccard between candidate profile.md/timeline.md and session facts |
| `embedding` | `EmbeddingSimilarity` | cosine over `IEmbeddingProvider`; no-op by default |
| `graphConsistency` | `GraphConsistency` | self-reference veto, small positive otherwise |
| `negative` | `NegativeAssertions` | one strong negative caps confidence below hard threshold |

Add a producer by implementing `ISignalProducer`; the signal kind must appear in
`ResolutionPolicy.SignalWeights` to contribute to confidence.

## Disk layout

```
<entity>/.resolution/incoming.yaml     inbound edges (owned by ResolutionActor)
<entity>/.resolution/promotions.log.yaml  append-only state transitions
<host-entity>/.resolution/outgoing.yaml   soft/hard link proposals (SidecarIntentSink)
.agctor/resolution.yaml                   per-project policy
sessions/<sessionId>/summary.yaml         per-session facts + mentions feed
```

Every YAML file is designed to be git-diff-friendly.

## Ingest bridge

`IResolutionIntentSink` is the seam. Implementations:

- `NullResolutionIntentSink` — default when the subsystem is self-contained (tests, CLI).
- `SidecarIntentSink` — writes `outgoing.yaml` next to the mention's host entity. Safe during
  rollout: no narrative markdown is touched.
- `MemoryIntentBridgeSink` — writes JSON proposals under `.agctor/runtime/resolution/intents/`
  shaped like PRD-016 `memoryIntents` so a future ingest runner can replay them.
- `CompositeResolutionIntentSink` — fans a draft out to the sidecar + bridge at the same time
  (default wiring in the DI extension).

## Host wiring

`AgctorSDK.Extensions`/`AgctorSDK.Core.DependencyInjection.ResolutionServiceExtensions.AddAgctorResolution()`
registers signal producers, the addressing scheme, metrics, `MentionObservationPublisher`,
`SessionSummaryEmitter`, `SessionMentionAccumulator`, and a `ResolutionBootstrapper` that spawns
the supervisor for a project. The Host calls `bootstrap.StartAsync(projectRoot, projectId)` once
the actor runtime is initialized and the `.agctor/resolution.yaml` file watcher takes care of
hot-reloading policy changes without a restart (`ResolutionBootstrapper` → `ReloadPolicy`).

The Host also registers `IResolveSpanSink` as a
`ResolveSpanTraceSink` so every resolved candidate shows up on the playground timeline as a
`pm.playground.resolve` span with Input / Evidence / Outcome cards.

## Review surface

- HTTP endpoints under `/api/project-memory/resolution/` (`pending`, `promote`, `demote`,
  `reject`, `metrics`) — see `ResolutionReviewController`.
- Razor page `Pages/Dashboard/ProjectMemory/ResolutionReview.cshtml` renders pending soft links,
  surfaces the signal table, and calls the controller for each row.

## Annotator

`ResolutionAnnotator` turns persisted edges into inline markup (`Raha (soft-linked 72% → raha)`
/ `Raha (→ raha)`). It is used in `ProjectMemoryPipelineRunner` to decorate person-query answers
so the final assistant text honors the link grade.

## Disabling / enabling

`ResolutionPolicy.Enabled` defaults to `false` in code; the sample project ships with
`resolution.yaml.enabled: true` so dogfood traffic exercises the whole pipeline. Flipping it off
turns the subsystem into a silent no-op without removing the DI registrations.
