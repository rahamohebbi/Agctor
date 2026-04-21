# PRD-018: Cross-session entity resolution (soft → hard links with evidence)

> **Note on numbering:** PRD-017 was already allocated to Playground hierarchy. This PRD was originally requested as “PRD-017” but is filed as **PRD-018** to avoid collision.

## 1. Overview

Today an AGCTOR project stores each canonical person as a folder under `people/<entityKey>/` with an `entity.yaml` plus narrative markdown (`profile.md`, `relationships.md`, …). Mentions inside a scenario — for example `scenarios/people/people/ryan/relationships.md` naming **Raha** as Ryan’s father — are **plain text**. The system has no first-class way to **detect** that this local mention is the same real-world entity as project-root `people/raha/`, and no way to **evolve** that detection across sessions as more evidence arrives.

PRD-018 introduces an **actor-first entity-resolution subsystem** that:

1. Treats every mention of an entity as a first-class event.
2. Accumulates **weighted, inspectable evidence** linking mentions to canonical entities.
3. Stages linkage in two visible grades — **soft link** (hypothesis, reversible) and **hard link** (canonical, append-only demotion only) — with a **promotion audit** that records *why* we believed the hypothesis at the moment of promotion.
4. Runs detection **in-session** (fast, recency-rich) and **across sessions** (full registry view) as two cooperating actor pipelines, so the Raha/Ryan case — where session B alone cannot reach the conclusion — is still resolved.

The core principle is to *use* the actor model rather than bolt a synchronous resolver onto the ingest path: evidence is a message stream, the per-entity state is an actor, the background reconciler is an actor, and the existing `person-extractor → ingest` flow stays the only path that writes markdown.

## 2. Goals

### 2.1 Actor-owned resolution state

1. A **`ResolutionActor`** (one per canonical entity, `res:<entityKey>`) owns that entity’s inbound soft/hard links and the evidence log. It is the only component that mutates `.resolution/` sidecars for that entity.
2. A **`ReconcilerActor`** (one per project, `rec:<projectId>`) watches for new session summaries and new mentions, and dispatches **ResolveCandidate** messages to the relevant `ResolutionActor`(s).
3. A **`MentionIndexActor`** (one per project, `midx:<projectId>`) maintains a fast lookup from surface forms (names, aliases, pronoun co-ref heads) to entity candidates, with no authority of its own.
4. All three are spawned via the existing `IActorRuntimeAdapter` so `InMemory`, Orleans, or Proto.Actor backends work unchanged.

### 2.2 Graded linkage with evidence

1. Every link is either **soft** or **hard**, carries a **confidence** in [0, 1], and references an **evidence log** that grows monotonically.
2. **Soft links** are query-visible but **non-authoritative** — they do not feed cross-entity aggregates unless the caller explicitly opts in and passes a threshold.
3. **Hard links** are created only by a **promotion event** (auto above a configurable threshold with no negative signals, or explicit operator confirmation). The promotion snapshots the deciding signals so future audits are reproducible even if scoring logic changes.
4. **Demotion** (hard → soft) and **rejection** append new rows; nothing deletes history.

### 2.3 Cross-session detection

1. When a session ends (or hits a checkpoint), a compact **`SessionSummary`** message is emitted with per-mention facts extracted during the session.
2. The `ReconcilerActor` consumes summaries and runs the resolver signal ensemble (§5.3) across new mentions × existing entities, so conclusions that *only* become visible with the full registry (e.g. Session B’s “Raha” = project-root `people/raha`) are still reached.
3. Per-entity `ResolutionActor`s accumulate evidence across sessions without any session needing to be “aware of” another.

### 2.4 Human-in-the-loop and auditability

1. Playground (and later a dedicated review surface) exposes **Input / Evidence / Outcome** sections for every resolve step, re-using the PRD-016 trace-drill-down pattern.
2. Every promotion or demotion records *actor* (`auto` or `user:<id>`), *reason*, *confidence snapshot*, and *signal snapshot* on disk so the decision is git-reviewable.

## 3. Non-goals

1. **Replacing the ingest path.** `person-extractor → ingest` is still the only writer of markdown; the resolver emits **intents** that ingest materializes.
2. **Probabilistic merge of entity *contents*.** Hard-linking `ryan.family[0]` to `people/raha` does **not** copy, merge, or dedupe profile text between folders. Content merges are a future PRD.
3. **Cross-project resolution.** Resolution is scoped to a single project; linking across project roots is out of scope.
4. **Silent deletion.** No operation removes evidence. Forgetting is modeled as a tombstone with redaction.
5. **Fully automated promotion without configuration.** The auto threshold must be explicit and overridable per project.

## 4. Current behavior (baseline)

- Entities are discovered from disk by `AgctorSDK.Core/ProjectMemory/Loading/EntityRegistry.cs`, which reads `entity.yaml` (`EntityKey`, `DisplayName`, `Aliases`) and lists required documents.
- `person-extractor` emits `memoryIntents` JSON that `ingest` writes under `people/` or `scenarios/<id>/people/` (see PRD-016 §4). There is no post-ingest step that links mentions in freshly written documents to other entities.
- Relationships are free-form markdown (`samples/people-project/scenarios/people/people/ryan/relationships.md` contains the literal string `- Raha`). Nothing tells the system this `Raha` is `people/raha`.
- Trace spans already carry `timelineDetailJson` for playground steps; PRD-016 made **Input / Outcome** a convention for drill-down.

## 5. Requirements

### 5.1 Actor topology

| ID | Requirement |
| --- | --- |
| A1 | **One resolution actor per entity.** Spawn id: `res:<projectId>:<entityKey>`. Owns `.resolution/incoming.yaml` and `.resolution/promotions.log.yaml` for that entity. |
| A2 | **One reconciler actor per project.** Spawn id: `rec:<projectId>`. Receives `SessionSummary`, `MentionObserved`, and `ForceReconcile` messages and fans out `ResolveCandidate` to the relevant resolution actors. |
| A3 | **One mention-index actor per project.** Spawn id: `midx:<projectId>`. Pure projection over `EntityRegistry` + aliases + recent soft links. Answers `LookupBySurface(text)` with a ranked candidate list. Stateless across restarts (rebuilt on startup from registry snapshot). |
| A4 | **Supervisor.** `ResolutionSupervisorActor` (`ressup:<projectId>`) spawns and restarts the above, and is the DI entry point for the subsystem. On `Faulted` state, children are rehydrated from disk, not from last in-memory state. |
| A5 | **Backpressure.** Reconciler must apply a bounded work queue per project; excess `ResolveCandidate` requests coalesce by `(entityKey, mentionId)` within a configurable window (default 2 s) so a chatty session cannot starve others. |

### 5.2 Messages (protocol)

All messages are plain POCOs under `AgctorSDK.Core/ProjectMemory/Resolution/Messages/`. Field names are indicative.

| Message | Direction | Payload (indicative) |
| --- | --- | --- |
| `MentionObserved` | extractor → reconciler | `mentionId`, `scope` (`projectRoot` / `scenario:<id>`), `surfaceForm`, `sourcePath`, `span`, `withinEntityKey`, `field`, `sessionId`, `turnId` |
| `SessionSummary` | session lifecycle → reconciler | `sessionId`, `projectId`, `mentions[]`, `assertedFacts[]`, `negativeAssertions[]`, `closedAt` |
| `ResolveCandidate` | reconciler → resolution actor | `mentionId`, `candidateEntityKey`, `registrySnapshotId`, `signalsHint[]` |
| `EvidenceAppended` | resolution → bus | `edgeId`, `signal`, `newConfidence`, `state` |
| `PromotionRequested` | reconciler or UI → resolution | `edgeId`, `reason`, `requestedBy` |
| `LinkStateChanged` | resolution → bus | `edgeId`, `from`, `to`, `by`, `snapshot` |
| `IngestIntentDraft` | resolution → extractor-ingest bridge | proposed delta for mention site (e.g. add `softLinkTo` or `entityRef`) |
| `DemotionRequested` | UI or reconciler → resolution | `edgeId`, `reason`, `requestedBy` |
| `LookupBySurface` / `LookupResponse` | any → mention-index | `text`, `scope`; returns `candidates[]` |
| `ForceReconcile` | admin → reconciler | optional `entityKey`, optional `since` timestamp |

`IngestIntentDraft` is the seam with PRD-016: resolution never writes markdown itself; it produces a draft that the existing ingest pipeline validates, applies, and audits.

### 5.3 Signal ensemble

| ID | Signal | Notes |
| --- | --- | --- |
| S1 | **Alias / display-name match** | Normalized (lowercase, diacritic-stripped, whitespace-collapsed) compare against `DisplayName` + `Aliases`. |
| S2 | **Surface-form uniqueness** | 1 / number-of-entities-matching-this-surface; boosts confidence when a name is rare in the project. |
| S3 | **In-session coreference** | “his father” → named head resolved within session transcript context; requires session has at least one prior mention with that role. |
| S4 | **Attribute overlap** | Jaccard / edit-distance over structured fields in `profile.md` / `timeline.md` (birthdate, workplace, hometown). |
| S5 | **Embedding similarity** | Cosine over profile-text embeddings; tiebreaker when names clash. Provider-pluggable; absence must not be a veto. |
| S6 | **Graph consistency** | Does a tentative merge introduce contradictions (e.g. two incompatible birthdays, cyclic family ties)? |
| S7 | **Negative / contradictory** | Explicit user statements (“different Raha”), or contradicting facts. Single strong negative caps confidence below the hard threshold regardless of positives. |

Confidence = `Σ (weight_i × score_i)` clamped to [0, 1], then subject to the hard-veto rule. Weights are **per-project configuration** (`.agctor/resolution.yaml`) with sane defaults. Each signal record stores `kind`, `score`, `weight`, `rationale`, `producedBy` (actor type + version), and `inputsFingerprint` (hash of the inputs so re-runs are deterministic).

### 5.4 Disk layout

| Path | Purpose |
| --- | --- |
| `<entity>/entity.yaml` | Unchanged; `aliases[]` still authoritative. |
| `<entity>/.resolution/incoming.yaml` | Edges *pointing into* this entity. Source of truth for the `ResolutionActor`. |
| `<entity>/.resolution/promotions.log.yaml` | Append-only audit of state transitions (soft↔hard, rejections, demotions). |
| `scenarios/<id>/.resolution/outgoing.yaml` | Edges *leaving* this scope (mirror of remote `incoming.yaml` for indexing). |
| `.agctor/resolution.yaml` | Project-wide thresholds, weights, hard-veto rules, reviewer roster. |
| `sessions/<sessionId>/summary.yaml` | Compact checkpoint / end-of-session summary consumed by the reconciler. |

All files are text (YAML) so they show up in diffs and are easy to review. Keep payloads bounded the same way PRD-016 caps trace JSON: previews + truncation flags.

### 5.5 Edge record schema

```yaml
edgeId: "mention:scenario:people/people/ryan#relationships.family[0] -> entity:people/raha"
state: soft                # soft | hard | rejected | superseded
confidence: 0.72
createdAt: 2026-04-18T12:34:56Z
lastUpdatedAt: 2026-04-18T12:45:10Z
provenance:
  - sessionId: "sess-…"
    turnId: "turn-4"
    span: "person-extractor:memoryIntents[2]"
signals:
  - kind: aliasMatch
    score: 0.90
    weight: 0.25
    rationale: "‘Raha’ matches displayName of people/raha (case-insensitive)"
    producedBy: "AliasMatcher@1"
    inputsFingerprint: "sha256:…"
  - kind: uniqueness
    score: 0.80
    weight: 0.20
    rationale: "Only one entity with surface ‘Raha’ exists in project"
    producedBy: "SurfaceUniqueness@1"
    inputsFingerprint: "sha256:…"
negatives: []
promotions: []
```

### 5.6 Promotion and demotion rules

| ID | Rule |
| --- | --- |
| P1 | **Auto-promote to hard** iff `confidence ≥ hardThreshold` (default 0.90), `negatives == []`, no contradicting graph edge, and at least two **independent** positive signals (independence = different `producedBy` families). |
| P2 | **Operator promote.** From the review UI, passes `PromotionRequested { requestedBy: user, reason }`. Always allowed regardless of threshold; promotion is still recorded with the same snapshot shape. |
| P3 | **Demote or reject.** Either auto (new strong negative) or operator. Appends `{ from: hard, to: soft, reason }` or `{ to: rejected }`. Never deletes rows. |
| P4 | **Downstream on promote.** `ResolutionActor` publishes `LinkStateChanged` and emits `IngestIntentDraft` that rewrites the mention site from `softLinkTo: …` to `entityRef: …`. Existing ingest validation applies. |
| P5 | **Downstream on demote.** Symmetric intent to downgrade `entityRef` back to `softLinkTo` with the evidence pointer. |

### 5.7 UI / trace surface

| ID | Requirement |
| --- | --- |
| U1 | Add `pm.playground.resolve` trace spans with PRD-016-style **Input / Evidence / Outcome** sections (signals table in the Evidence section). |
| U2 | Playground transcript rendering surfaces soft links inline (e.g. `Raha ⟶ people/raha (72%)`) with a hover or details link to the evidence panel. |
| U3 | A **Resolution review** tab lists pending soft links sorted by `confidence × recency`, with Confirm / Reject / Needs-more-evidence actions. |
| U4 | `person-query` responses reflect soft-link grade: “Ryan’s father is **likely** Raha (72%)” vs hard-linked “Ryan’s father is Raha”. |

### 5.8 Configuration

`.agctor/resolution.yaml` (project-scoped, all optional with defaults):

```yaml
hardThreshold: 0.90
softThreshold: 0.55
signalWeights:
  aliasMatch: 0.25
  uniqueness: 0.20
  corefInSession: 0.20
  attrOverlap: 0.15
  embedding: 0.10
  graphConsistency: 0.10
vetoRules:
  - kind: explicitUserNegation
    action: capBelowHard
reconciler:
  coalesceWindowMs: 2000
  perEntityQueueSize: 32
  batchSize: 16
review:
  autoPromote: true
  requireReviewer: false
```

## 6. Acceptance criteria

1. **Within-session soft link (happy path).** A playground session where the user says “Ryan’s father is Raha” yields, after extractor + reconciler turns, a **soft link** `mention:…ryan#relationships.family[0] → people/raha` with `confidence ≥ softThreshold` and visible Input / Evidence / Outcome sections in the `pm.playground.resolve` trace span.
2. **Cross-session soft link.** Session A mentions Raha’s details in project root. Session B independently says “Ryan’s father is Raha”. Without either session knowing about the other, the reconciler — after session B’s summary — emits the same soft link as (1). No manual intervention required.
3. **Auto-promotion to hard.** Additional corroborating evidence (e.g. attribute overlap + stable coreference across two more sessions) raises confidence past `hardThreshold`. The resolution actor emits `LinkStateChanged` with `{ from: soft, to: hard, by: auto }`, `.resolution/promotions.log.yaml` gains a row with a signal snapshot, and the mention site is rewritten from `softLinkTo` to `entityRef` via ingest.
4. **Operator rejection.** From the review tab, operator rejects a soft link. Edge state becomes `rejected`, the reconciler no longer proposes it on subsequent runs until new evidence appears, and existing downstream aggregations drop the edge at their next refresh.
5. **Demotion.** When a new strong negative is ingested for a previously hard edge, state transitions to `soft` with a demotion row; ingest rewrites `entityRef` back to `softLinkTo`. Nothing is deleted from history.
6. **No-op on ambiguous surfaces.** If a surface form matches multiple entities with comparable scores and no tiebreaker, no soft link is created; the mention is recorded in `MentionIndexActor` with `needsDisambiguation: true` and appears in the review tab.
7. **Resilience.** Killing the host mid-run and restarting rehydrates actor state from disk; in-flight mentions are re-emitted from the last session summary. No duplicate evidence rows (idempotency keyed on `edgeId + inputsFingerprint`).
8. **Tests.**
   - Unit tests for each signal producer (in `AgctorSDK.Core.Tests/ProjectMemory/Resolution/`).
   - Unit tests for promotion / demotion state machine.
   - Integration test for reconciler end-to-end using the `InMemory` runtime (in `AgctorSDK.Core.IntegrationTests`).
   - Host integration test for the `pm.playground.resolve` trace span shape.

## 7. Security and data

- Evidence can quote source text; apply the same size caps and truncation flags PRD-016 enforces for trace JSON.
- Forgetting is a **tombstone**: evidence rows are marked `redacted: true` with the quoted text replaced by a hash; the edge and its audit trail survive.
- No new network exfiltration surface; embedding calls (S5) reuse the existing LLM/provider configuration and must be toggleable off per project.

## 8. Open questions

1. Should **auto-promotion** require two independent *signal families* (as in P1) or two independent *evidence events* (different sessions / different documents)? The stricter reading is safer; the looser one unblocks small projects.
2. Should a **ContentMergeActor** be in scope later (PRD-019?) to reconcile narrative text between a scenario-scoped `ryan` and a project-root `ryan` once linked, or does that always stay a human job?
3. How do we expose **negative evidence capture** in the composer UX — a slash command (`/not-same`), a transcript reaction, or only via the review tab?
4. For **multi-tenant** deployments (future), should `ResolutionActor` grain keys include a tenant segment, and should promotions require a reviewer claim?

## 9. Why this leans on the actor model

- **Per-entity isolation.** Evidence for `people/raha` is mutated only by `res:<proj>:raha`. No locks on YAML files, no races when two sessions propose links simultaneously.
- **Message log is the audit trail.** Evidence is literally the stream of messages the actor received, projected to disk. That matches what operators want to review.
- **Backpressure is natural.** Coalescing on `(entityKey, mentionId)` inside the reconciler mailbox is trivial; the same pattern in a synchronous pipeline would need explicit queues and debouncing.
- **Location-transparent.** The same design works on `InMemory`, Orleans, or Proto.Actor adapters because we only use `IActorRuntimeAdapter` primitives (`SpawnActorAsync`, `SendMessageAsync`, `StopActorAsync`).
- **Supervised failure.** A broken signal producer fails its actor; the supervisor restarts it from the last disk snapshot; partial progress survives because evidence is append-only.
- **Hot-swap of resolution policy.** Changing `.agctor/resolution.yaml` signals a `ReloadPolicy` message; the reconciler re-weights pending edges without restarting the host.
