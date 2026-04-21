# PRD-018 Agent Configuration and Collaboration Guide

This guide explains how the newly added PRD-018 resolution subsystem should be configured, how it works with existing project-memory agents, and what (if anything) you need to change in your scenario flow.

## Quick answer for your current flow

Your current flow:

`Chat input -> Router -> PersonaCall -> PersonaCall -> Output`

is still valid for PRD-018.

You do **not** need to add new Scenario Flow Designer nodes for resolution actors (`ResolutionSupervisorActor`, `ReconcilerActor`, `MentionIndexActor`, per-entity `ResolutionActor`). Those run as background actor services, not as `PersonaCall` graph nodes.

## How agents now work together

PRD-018 keeps the same high-level app behavior:

1. `person-extractor` produces memory intents.
2. ingest writes markdown / entity files.
3. `person-query` answers from project memory.

PRD-018 adds an **actor-owned resolution layer** in parallel to the persona graph. It does not replace ingest; it observes what ingest already produced and proposes or hardens links with evidence. The steps below are the happy-path pipeline (details live in `prd-018-agctor-prd.md` and the Core module under `AgctorSDK.Core/ProjectMemory/Resolution/`).

1. **Mention and session events enter the reconciler**  
   After `person-extractor` output is parsed into `memoryIntents` and routed, the ingest path can publish **`MentionObserved`** messages: each retained intent yields one or more structured mentions (surface text, host `entityKey`, optional scenario scope, session/turn ids when the Host passes them). On session end or checkpoint, the Host can also emit a **`SessionSummary`** so mentions and facts from the whole session are replayed once—this is what makes cross-session linking work when a single turn did not have enough context.

2. **`ReconcilerActor` finds candidates and fans out work**  
   The reconciler is one actor per project. It uses **`MentionIndexActor`** (a read-only projection over registry display names and aliases) to map a surface string to candidate canonical entities. For each `(mention, candidate)` pair it sends **`ResolveCandidate`** to the owning **`ResolutionActor`** for that candidate’s `entityKey`, with optional session facts and negatives attached for richer signals. It also **coalesces** duplicate `(mentionId, candidateKey)` work inside a time window so a chatty session cannot flood the same actor.

3. **`ResolutionActor` scores, persists, and may auto-promote**  
   Each canonical entity has its own resolution actor; it is the **only writer** of that entity’s `.resolution/incoming.yaml` and append-only promotion log. On each `ResolveCandidate` it runs the configured **signal ensemble** (alias match, surface uniqueness, attribute overlap, graph checks, optional embeddings, negatives, etc.), merges new signals into the edge (idempotent by signal kind + input fingerprint), recomputes **confidence**, and updates edge state (**soft** by default when above the soft threshold; **hard** only when promotion rules pass, e.g. auto-promote with enough independent signal families and no veto). Nothing deletes history—rejections and demotions append rows.

4. **Downstream artifacts: sidecar, bridge proposals, and traces**  
   When an edge crosses meaningful thresholds, resolution does **not** write narrative markdown itself. It emits **`IngestIntentDraft`** through **`IResolutionIntentSink`**: by default the composite sink writes **`outgoing.yaml`** next to the mention’s host entity (git-diffable proposals) and JSON rows under **`.agctor/runtime/resolution/intents/`** in a `memoryIntents`-compatible shape for a future ingest replay. Separately, a **`pm.playground.resolve`** trace span can record **Input / Evidence / Outcome** so operators can audit what the resolver believed and why.

5. **Human review and explicit state changes**  
   Pending **soft** links can be listed and sorted (e.g. by confidence × recency). Operators call **promote** (soft → hard), **demote** (hard → soft), or **reject** (terminal for that hypothesis until new evidence). Those actions go through the same **`ResolutionActor`** mailbox as automatic work, so races with the reconciler are avoided. The UI and API are optional; the subsystem still runs if you only use files and traces.

So your two `PersonaCall`s can stay:

- `PersonaCall #1`: extractor path (`person-extractor`)
- `PersonaCall #2`: query path (`person-query`)

The resolution subsystem enhances what happens around them, without requiring a new graph shape.

## Minimal configuration checklist

### 1) Keep extractor and query agents configured

Sample agents:

- `samples/people-project/.agctor/agents/people/person-extractor.agent.yaml`
- `samples/people-project/.agctor/agents/people/person-query.agent.yaml`

Make sure:

- extractor still emits valid `memoryIntents` JSON
- extractor uses stable `entityKey` and relationship attributes
- query agent remains read-only (`write_document` denied)

### 2) Enable PRD-018 policy

File:

- `samples/people-project/.agctor/resolution.yaml`

Key fields:

- `enabled`
- `softThreshold`
- `hardThreshold`
- `signalWeights`
- `reconciler.coalesceWindowMs`
- `review.autoPromote`

### 3) Ensure Host wires resolution services

PRD-018 expects host startup wiring that:

- registers `AddAgctorResolution()`
- bootstraps `ResolutionBootstrapper` after actor runtime init
- registers resolve trace sink (`pm.playground.resolve`)

### 4) Use review operations for human-in-the-loop

Review endpoints (host):

- `GET /api/project-memory/resolution/pending`
- `POST /api/project-memory/resolution/promote`
- `POST /api/project-memory/resolution/demote`
- `POST /api/project-memory/resolution/reject`

Dashboard page:

- `/Dashboard/ProjectMemory/ResolutionReview`

## Do you need to modify the scenario flow diagram?

Usually: **No**.

Keep the current sequence unless you want different UX behavior.

You may optionally adjust flow if you want stricter orchestration:

- **Recommended default (no change):**
  - Router -> `person-extractor` -> `person-query`
- **Optional split behavior:**
  - Add Router conditions to skip extractor for pure read-only questions.
- **Optional review-oriented flow:**
  - Keep graph same, but drive resolution review from dashboard/API rather than adding another PersonaCall.

## Brief context: why no new node is needed

Scenario Flow Designer models user-visible persona execution. PRD-018 adds actor-level memory resolution infrastructure that:

- consumes mention/session events
- computes evidence asynchronously
- persists link state and audit logs
- enriches traces and query narration

Because it is infrastructure-side and actor-owned, it should stay outside the persona graph.
