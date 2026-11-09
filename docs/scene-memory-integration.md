# Scene Memory integration for Cynelo

Scene Memory runs in the AGCTOR Host as an HTTP service. Cynelo needs no AGCTOR assembly, shared database, or dashboard code. Its API should call this private service and expose the results through Cynelo's own authenticated project routes.

- API root: `/api/scene-memory/v1`
- Dedicated interactive documentation: `/swagger/scene-memory`
- Dedicated OpenAPI document: `/swagger/scene-memory-v1/swagger.json`
- Capability discovery: `GET /api/scene-memory/v1/capabilities`

This version isolates the integration **contract**, not the deployment process. Existing dashboard endpoints remain compatible. Test agents, evaluation tools, CodeGraph, RAG administration, and other Host features are excluded from this OpenAPI document.

## Ownership and identities

Cynelo owns Studio assets, users, project authorization, and media. AGCTOR owns the scene's identities, proposed changes, applied history, and continuity queries.

An external workspace is identified by `(application, projectId, storyId)`. These are opaque, case-sensitive IDs, not credentials. Cynelo must authorize access to its project before forwarding a workspace request. This module does not add authentication or tenant authorization to the existing Host; keep it behind the trusted backend/private network until that separate boundary is configured.

Each Studio link uses:

| Field | Meaning |
| --- | --- |
| `instanceKey` | Stable identity of this occurrence in the story, supplied by Cynelo. |
| `assetId` | Character, Object, or Location Studio asset ID. Multiple instances can reference the same asset. |
| `version` | Positive, monotonically increasing integer for that instance's synchronized metadata. |
| `entityId` | AGCTOR's stable scene identity. Supply it when linking something already discovered. |
| `defaults` | Design/reference metadata. These do not become current facts or overwrite history. |

Names never implicitly merge two Studio identities. When a name already exists, explicitly link its `entityId`, or choose a distinct instance name. A character's scene name stays stable after linking; Studio renames update `studio.displayName`. The interpreter receives that display name as an alias, along with reference defaults. A saved entity without `studio` is unlinked; it may have been discovered from a prompt or created manually.

Updating Studio metadata requires a greater version. An exact same-version retry is a no-op. An older version, changed content at the same version, a different asset for an existing instance, or a second link to an already-linked entity returns `409`. Linking validates the entity belongs to that workspace and has the same kind.

For Cynelo's existing Character Studio, map `Character.Id` to `assetId` (not a portrait media ID), and use a stable story occurrence key for `instanceKey`. Map selected fields from `CharacterVersion.Brief` to defaults. Approved immutable versions can use `CharacterVersion.VersionNumber`; if synchronizing edits within a draft or rolling back to an older design, use a separate increasing synchronization version. Cynelo retains the mapping to its `CharacterVersion.Id` and approved portrait/voice assets. Authorization still uses Cynelo's character owner and project rules.

## First complete workflow

### 1. Connect the story

`POST /api/scene-memory/v1/workspaces/connect`

```json
{
  "name": "Mountain adventure",
  "application": "cynelo",
  "projectId": "project-123",
  "storyId": "story-456"
}
```

Save the returned `id` as the AGCTOR workspace ID. Repeating this connection returns the same workspace, including after a restart. The supplied name is only used when creating a workspace.

List connected stories with `GET /api/scene-memory/v1/workspaces?application=cynelo&projectId=project-123`. Both parameters are required and match the external connection exactly. This filtering is not authentication: Cynelo's trusted backend must derive the authorized namespace and keep AGCTOR private. The Cynelo bridge implements that ownership boundary and exposes `/api/scene-memory/stories` to its authenticated clients.

To adopt an existing AGCTOR story, include `workspaceId` in this request. It preserves all entities and history. A workspace cannot be rebound to a different external story, and an external story cannot be connected to a second workspace. Connecting a legacy workspace changes its revision, so outstanding previews should be regenerated.

### 2. Connect a Studio character

`PUT /api/scene-memory/v1/workspaces/{workspaceId}/studio-entities/maya-main`

```json
{
  "assetId": "character-42",
  "version": 1,
  "name": "Maya",
  "kind": "Character",
  "defaults": { "appearance": "Adult explorer in a blue coat" }
}
```

Use the same endpoint with `kind: "Object"` or `"Location"`. IDs are supplied by Cynelo; images and media remain in Cynelo. This API does not fetch or analyze Studio images. Defaults are a full replacement map per version (omitting them means an empty map), limited to 20 entries, with key/value limits of 100/1000 characters.

### 3. Interpret a prompt with a new object

`POST /api/scene-memory/v1/workspaces/{workspaceId}/previews`

```json
{
  "prompt": "Maya holds a red mug.",
  "sceneName": "Kitchen",
  "shotName": "A new prop",
  "durationSeconds": 5,
  "model": "gemma4:latest"
}
```

The result contains `preview`, `proposed` identities, and `proposedChanges`. Use these to render Cynelo's review screen. Nothing has been saved to the story. New entity IDs in the proposed result are provisional; obtain committed IDs from the apply response before creating persistent links.

To correct the interpretation, `PUT /workspaces/{workspaceId}/previews/{previewId}` with `{ "extraction": ... }`, using the extraction structure in the preview. Corrections run through the same validation. Unresolved ambiguity questions must be resolved by clarifying the prompt and generating a new preview.

### 4. Apply the reviewed preview

`POST /api/scene-memory/v1/workspaces/{workspaceId}/previews/{previewId}/apply`

No body is required. The response supplies committed identities and the new revision. The operation is atomic and safe to retry after a lost response, including after a restart. Any intervening story or Studio change makes the preview stale and returns `409`; regenerate the preview against the new context.

### 5. Link the discovered mug to Object Studio later

`PUT /api/scene-memory/v1/workspaces/{workspaceId}/studio-entities/mug-main`

```json
{
  "assetId": "object-7",
  "version": 1,
  "name": "Ceramic studio mug",
  "kind": "Object",
  "entityId": "COMMITTED_MUG_ENTITY_ID",
  "defaults": { "material": "Ceramic", "color": "Red" }
}
```

This attaches a Studio asset to the existing identity. The mug's holder, events, and history remain intact. A Studio default such as `color: red` is separate from the mug becoming blue during a shot.

### 6. Render Cynelo's timeline

`GET /api/scene-memory/v1/workspaces/{workspaceId}/timeline`

The response includes:

- `entities`: the current identity/Studio catalog, indexed by `id`. Studio metadata is current, not a historical snapshot.
- `shots`: ordered applied prompts, with scene/shot names, duration, prompt, and sequence boundaries.
- `shots[].changes`: before/after facts for properties changed in that shot; null means unset.
- `shots[].stateAfter`: all active recorded facts at that shot's final sequence.
- `shots[].events`: story moments, including occurrence modes and participant IDs.
- `shots[].introducedEntityIds`: identities discovered in that shot.

Map `entityId` and `relatedEntityId` to the catalog to render names or Cynelo asset links. Group contiguous shots by `sceneName`; do not regroup all shots with equal scene names across the whole story. Sequence numbers are recorded changes, not video timestamps. Never infer a saved duration from the prompt text. Empty shot/scene names can receive display-only fallbacks.

`POST /workspaces/{workspaceId}/query` with `{ "entityId": "...", "atSequence": 12 }` supports historical queries; omit `atSequence` for current facts. Character queries also expose reverse possession relationships.

## Errors and runtime limits

The versioned controller uses HTTP status codes and `application/problem+json`, with `code`, `detail`, and `traceId`. Interpretation/validation errors include structured diagnostics identifying source passages. Request-shape validation uses ASP.NET validation problem responses (`errors`); clients should handle both forms.

| Status | Client behavior |
| --- | --- |
| 400 | Correct invalid request fields. |
| 404 | Resolve the workspace/entity identity. |
| 409 | Refresh stale versions/previews or resolve an identity conflict. |
| 422 | Show interpretation diagnostics; let the user correct the prompt or review. |
| 502 / 504 | Show model availability/timeout information; allow retry. |

Preview requests remain request-bound and may take several minutes. Configure the Cynelo backend's HTTP timeout accordingly. Disconnecting/canceling the request cancels interpretation. Preview documents and interpretation checkpoints remain in memory for up to 20 minutes and do not survive Host restarts. Applied state, Studio links, and external workspace connections are persisted in SQLite.

The capabilities endpoint explicitly reports `persistentJobs: false`. Durable jobs, reconnectable progress, external asset deletion/rebinding workflows, authentication, and deploying Scene Memory in its own minimal Host are later milestones, not implied by this contract. Avoid using multiple Host instances until preview routing/persistence and storage deployment are designed for that topology.

## Verification

`SceneStudioIntegrationTests` exercises a simulated Cynelo client over HTTP: connect Studios, interpret a prompt with a newly discovered prop, review/edit, apply, link that prop to Object Studio, and retrieve timeline/query results. It also checks version conflicts, project/story identity separation, historical state, and dedicated OpenAPI isolation. A deterministic interpreter keeps these contract tests independent of local model availability; they do not certify model accuracy or implement Cynelo's UI.

`SceneStudioPersistenceTests` verifies restart durability and concurrent connection retries against SQLite.

## Scenario workflow: continuity and approved video drafts

The dedicated Scene Memory HTTP surface now supports the next-shot workflow:

- `GET /api/scene-memory/v1/workspaces/{id}/context` returns the revision, current facts (including manual changes), identities/design bindings, and previous shot identity/name. Events and removed facts are not current state.
- `POST /api/scene-memory/v1/workspaces/{id}/previews` accepts optional `expectedRevision`. When supplied, mismatched context is rejected with 409 before interpretation, and the interpreter's snapshot revision is checked again before returning its preview. Existing clients can omit it. Explicit apply retains its revision guard and idempotence.
- `POST /api/scene-memory/v1/workspaces/{id}/shots/{shotId}/video-prompt` accepts `{ "expectedRevision": "<context or timeline revision>" }`. Only applied shots can be prepared. It returns a deterministic prompt, source revision/shot ID, historical start/end facts, relevant entities and notices. Preparation is read-only and makes no model call. A missing shot returns 404; a changed revision returns 409.

The prepared prompt separates the original shot action, latest linked design descriptions, starting facts and approved ending facts. Related props/locations are included through recorded relationships; explicit removals prevent prior conditions from silently reappearing. Later facts and later participants are excluded when preparing an earlier shot. Existing design snapshots preserve historical metadata and participant membership, but no longer determine appearance for new video attempts. Older shots work without a pinning step.

Cynelo proxies these through its owner-authorized `/api/scene-memory/stories/{storyId}/context` and `/shots/{shotId}/video-prompt` routes. Its UI reviews the result, confirms replacement of an existing composer prompt, and checks the revision again before importing. No video is generated automatically; image references, models and duration remain editor choices. Editing the video draft does not mutate the story. AGCTOR remains a separate HTTP companion without assembly references in Cynelo.

## Atomic Studio link management

`PUT /api/scene-memory/v1/workspaces/{workspaceId}/entities/{entityId}/studio-link` accepts `expectedRevision`, optional `instanceKey`, and optional `design` (the existing Studio sync request shape). Omit `design` and `instanceKey` to unlink. Supply both to replace or link a design to the existing entity. The entire operation runs in one store transaction: revision, kind and instance uniqueness checks happen before writing. Replacing a link does not detach it first. Invalid or stale requests leave the existing binding intact.

The entity ID/name, state facts, prompts and history are preserved. Only its Studio reference changes. No library assets are deleted. Changed bindings invalidate outstanding previews/video drafts through the existing workspace revision. Clients must refresh after a conflict, including an uncertain response whose change may already have succeeded. Cynelo's owner-authorized bridge resolves replacement designs itself and never accepts arbitrary Studio metadata from the browser.


### Latest linked designs for shots

New generations use the latest library design linked to each participating scene entity. Link, change-design and unlink controls are available in Cynelo's story panel and directly in each shot's participant list. A link is shared across the story, so changing it affects future generations of all shots involving that entity. Completed videos and their archived take inputs stay unchanged.

- Timeline shots expose `participants`, containing current entity links relevant to that shot. Historical action/state and membership remain independent of current appearance. New entities from later shots cannot leak into earlier ones.
- Cynelo refreshes owned Character/Object/Location library records when loading timeline/context and preparing a video. Descriptions and individual content-addressed photo IDs are synchronized; equal photo counts do not imply equal photos. No-op refreshes keep the revision stable.
- Opening a new video draft adopts library changes automatically. Model preview and generation require the reviewed revision. A library or story change after review returns 409 instead of silently substituting inputs. Revision-checked writes prevent refresh from restoring a concurrently unlinked/replaced design.
- Unavailable linked designs stay visible for repair; generating a shot using one requires relinking or unlinking it. Missing designs unrelated to that shot do not block it.
- `designSnapshots`, `GET .../shots/{shotId}/designs`, and `PUT .../shots/{shotId}/designs` remain for historical inspection and compatibility. They no longer gate generation or freeze appearance. User-facing version comparison/pinning controls have been removed from the normal workflow. No existing snapshots or generated takes are deleted.
- AGCTOR knows the latest metadata synchronized by its client; it does not read Cynelo's library or media directly. Cynelo resolves and archives actual selected image bytes with each accepted take.

### Explicit duration in scene prompts

Preview resolves an explicit clip-length instruction (for example, “Create a 12-second cinematic video”) into `durationSeconds` when no planned duration is supplied. An explicit metadata value wins, including later timeline edits. Video-prompt preparation applies the same resolution to older shots with no stored duration without changing their history. Action timings such as “waits 12 seconds” are not clip durations. Conflicting explicit clip lengths return a validation error; set one planned duration or clarify the prompt. Clients must carry the resolved duration into generation and disclose unsupported lengths rather than silently rounding or clamping them.

### Video prompt debugger

On **Scene Memory → Shot and scene timeline**, select a shot and choose **Simulate video prompt**. It displays the assembled model-text simulation, the original action, the unmodified AGCTOR handoff, Cynelo's continuation stage, included current linked designs, and downloadable request/response JSON. It does not interpret text, call a video model, upload media, or modify story state.

`POST /api/scene-memory/v1/workspaces/{workspaceId}/shots/{shotId}/video-prompt/simulate` takes `expectedRevision`, optional `existingClips` (default 0), optional `durationSeconds` (otherwise resolved from the shot), and up to eight `referenceLabels` in **actual upload order**. It reuses `SceneVideoPromptBuilder.Prepare`, including stale-revision checks. Unsupported/missing lengths require an explicit 1–15 whole-second simulation override. Output includes `handoff`, `modelPrompt`, `continuationPrompt`, `workflow`, `targetSeconds`, `modelSeconds`, warnings, and the formatter version.

The `cynelo-h3-2026-11-04` adapter is a compatibility snapshot of Cynelo's `ContinuationPrompt`, `ReferencePrompt`, and H3 duration rounding, with 60 golden parity cases produced from the original Cynelo source. It is deliberately separate from the generic handoff and introduces no cross-repository assembly dependency. Update these snapshots and parity fixtures whenever Cynelo's formatter changes; do not claim this simulation is an actual submitted workflow. AGCTOR cannot discover Cynelo's film, selected/uploadable image files, job settings, or later code changes. In particular, the current continuation branch classifies the handoff as already-happened context and appends a generic next action; this debugger reproduces and highlights that behavior rather than quietly changing generation.

For an actual job, Cynelo's recorded take `runs[].workflow` is authoritative: inspect the prompt inputs in the submitted graph. The base take prompt and pre-reference run prompt can differ from the tagged model input. This simulator helps compare those stages with explicit film/reference assumptions.

### Customize a video attempt before generation

Cynelo's shot generation and prompt preview dialogs offer **Customize prompt**. Users can edit the creative text manually, reset to the saved shot, or ask an installed Ollama chat model for an edit. Model suggestions are separate drafts: users review, optionally adjust, and accept them before choosing **Use changes**. Closing the editor discards its changes and cancels a pending rewrite. The final model prompt is then prepared again with the chosen reference images and continuation formatting.

- `GET /api/scene-memory/v1/prompt-models` lists installed completion models.
- `POST /api/scene-memory/v1/workspaces/{workspaceId}/shots/{shotId}/video-prompt/rewrite` accepts `expectedRevision`, `prompt` (up to 32,000 characters), `instruction` (up to 2,000 characters), and optional `model`. It returns `{prompt, model}` without writing state. It checks the shot revision before and after inference, bounds inference to three minutes including queue time, supports cancellation, and rejects truncated or malformed suggestions. The service advertises `video-prompt-rewrite`.
- Cynelo exposes this through its owner-scoped `/api/scene-memory/stories/{storyId}/shots/{shotId}/rewrite-prompt` route. Ollama remains configured in AGCTOR; no new endpoint setting or assembly dependency is introduced.
- Cynelo's `model-prompt` and `generate` requests accept optional `promptOverride`. Customized generation requires `expectedModelPrompt` matching a freshly assembled preview. Reference selection and duration still come from their existing settings, never from the rewritten prose. The immutable take records the edited base prompt and original `source` separately; the submitted workflow records the final tagged text.

These edits affect only that video attempt. They do not update Scene Memory facts, shot text, design versions, or reference images. The AGCTOR simulator starts from the shot handoff and latest synchronized links; use Cynelo's model preview and recorded workflow when debugging a customized take.


### Omitted props and designs linked after a shot

A story-level Studio link makes a design available; it does not include that design in every shot. The video draft and timeline participant list select entities mentioned by the saved action, introduced or involved in that shot’s approved changes/events, or needed by its physical continuity. Named late-linked designs still work even when the original interpretation omitted them. An explicit continuity-only instruction can inherit the preceding shot’s participants within the same scene; it never inherits the entire library/story snapshot. Shared locations and social/ownership links do not pull unrelated occupants or possessions into a shot. Adding, replacing or unlinking a design changes its appearance only in shots where it participates. Historical shot action, facts, design snapshots and completed videos remain unchanged, and facts from later shots never become an earlier shot's state.

Every interpreted script section now receives one source-based entity and state completeness review, including after a validation repair. The review checks for entirely omitted common-noun props, settings and relationships, instead of inspecting only entities the first response returned. The prompt explicitly handles separate matching props, possession, aliases, and production-only, negated or hypothetical items. Extraction and review together retain the four-call bound and two validation-repair allowance per section; this can add one model request to sections that previously skipped review. Model interpretations still require user review and apply before they become saved story state.

Restart the AGCTOR Host to load these changes. In Cynelo, refresh the story and reopen Generate video. A linked Sword can supply its reference image when the shot names it or its recorded physical continuity requires it, even if detection omitted the name. A forest scene about a woman does not acquire unrelated chipmunks, swords or dragons just because those designs are linked to the same story. Cynelo's eight-image generation limit still applies; all available choices remain visible for selection.
