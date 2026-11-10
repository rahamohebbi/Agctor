# Scene video workflow integration

Agctor owns planning, Scene Memory, durable run state, human questions and progress. Cynelo owns video generation, design images, media storage and film assembly. The `scene-video-chain` Scenario is the first durable `AgentLoop` workflow.

## Start from the dashboard

Open **Scenarios → Scene Memory — video sequence → Run workflow**. **New story** is selected by default: enter the complete action and durations, and the workflow creates fresh, empty Scene Memory automatically in the configured Cynelo studio. No existing story or library designs are copied. Choose **Continue an existing story** to reveal the story selector and explicitly reuse that story’s relevant continuity. The selector lists only stories available to the configured Cynelo integration owner. For example, 31 seconds with 15-second clips produces 15 + 15 + 1 seconds. This version supports 1–15 seconds per clip, 360 seconds total, and at most 24 clips.

The director routes each step through a specialist actor: planning, Scene Memory interpretation/commit, or Cynelo rendering. Actors are short lived; SQLite stores their authoritative progress. The dashboard shows the plan, ordered clip links, status, activity history and any pending question. Questions support choices and free text. Plan edits and scene clarifications inform the next model call. Operational notes are recorded; an offered action is required before retrying a render or extending its wait.

The Scenario designer exposes workflow guidance and retry limits. This initial `AgentLoop` supports the graph `ChatInput → AgentLoop → Output`; arbitrary compositions of durable loops with other node types are rejected. Use the runs API, not the synchronous conversation-flow API.

## Configure the private connection

Agctor settings (environment variables use double underscores instead of colons):

```json
{
  "Agctor": {
    "SceneMemory": { "StorePath": "/data/scene-memory.db" },
    "SceneVideo": {
      "StorePath": "/data/agent-runs.db",
      "WorkerEnabled": true,
      "RendererBaseUrl": "http://127.0.0.1:5080/api/agctor/render",
      "PollSeconds": 5,
      "WaitingPollSeconds": 30,
      "RenderTimeoutMinutes": 45
    }
  }
}
```

Agctor defaults to Cynelo's local rendering endpoint, `http://127.0.0.1:5080/api/agctor/render`; no URL configuration, account token or API key is needed for local development. Override `Agctor:SceneVideo:RendererBaseUrl` when Cynelo runs elsewhere. Cynelo accepts token-free requests from localhost by default. For Docker, use Cynelo's service address instead of loopback and explicitly set `AgctorRendering:TrustedNetworks` in Cynelo to the private network used by the Agctor container (for example, `["172.20.0.0/24"]`). Other connections receive `403`; forwarded address headers do not grant access. Keep these endpoints off public reverse proxies, including proxies that connect from localhost.

Cynelo uses its single-user `local` workspace by default. If you already have stories under a signed-in account, set `AgctorRendering:Owner` in Cynelo to that account's email once. This is a server-side owner selection, not a credential supplied by Agctor. The selected owner must own the story. Cynelo's existing Scene Memory connection must point back to Agctor. Normal Cynelo pages and account APIs retain their existing sign-in behavior.

The local Cynelo development configuration selects `admin@cynelo.local`, matching the development studio. A `404` with code `story_not_found` means Cynelo was reached but the configured integration owner cannot see the selected story; verify the owner instead of changing the renderer URL. This error pauses the run with configuration guidance. After correcting the setting, retry the existing step using its original request key.

Persist both Agctor database files on a Docker volume. Cynelo persists render receipts and continuation frames in `AgctorRendering:StorePath`, defaulting to `agctor-render-jobs` beneath its workspace root; persist that directory with Cynelo's existing media/provenance storage. Keep Agctor's API on a private service network: its workspace IDs are not an authentication boundary. The integration endpoints use the connection restriction above and resolve stories, jobs and media only for the configured integration owner.

New sequences provision an isolated saved Cynelo project automatically. Continuing a story reuses its linked project, regardless of the film currently open in the editor. Subsequent clips require that project’s last clip to match the preceding workflow take. The bridge resolves linked reference images inside Cynelo and uses its own saved preceding clip for continuation; it never downloads arbitrary frame URLs supplied in a request.

## Cynelo calls Agctor

`POST /api/scenarios/scene-video-chain/runs` returns `202` with the run and its status URL:

```json
{
  "requestId": "client-generated-unique-request-id",
  "storyMode": "new",
  "prompt": "A traveller crosses a bridge, reaches home, and greets their family.",
  "totalSeconds": 30,
  "clipSeconds": 15,
  "reviewPlan": true
}
```

For a fresh story, send `storyMode: "new"` and omit `workspaceId`. To append shots to an existing story, send `storyMode: "continue"` and its `workspaceId`. Combining `new` with an existing workspace or omitting the workspace for `continue` is rejected. For older callers, omitting the mode while providing a workspace preserves the previous continuation behavior; omitting both defaults to a fresh story.

New-story admission first persists the run with an `initialize` phase. The worker provisions a Cynelo-owned story using its stable run ID, then checkpoints the resolved workspace ID, story ID and initial revision before planning. The unique workspace lock is updated in the same transaction as the run checkpoint. A lost provisioning response or restart reconnects to the same empty story; retrying admission with the same request ID returns the same run. The new story name is taken from the first 80 characters of the prompt without a model call. This step does not submit video or clear the current Cynelo film. The project actor then provisions a stable project before planning; it never clears the open editor.

`GET /api/agent-runs/stories` lists the configured Cynelo studio’s connected stories for the continuation selector. Each entry contains `workspaceId`, `storyId`, and `name`.

Optional `model` overrides the Scenario's planning model. Repeating the same `requestId` and inputs returns the original run; changing those inputs returns `409`. Only one active run may write a workspace at a time, including runs waiting for a person.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/agent-runs?scenarioId=scene-video-chain` | Latest 100 runs |
| `GET /api/agent-runs/{id}` | Current status, question, plan and ordered clip outputs |
| `GET /api/agent-runs/{id}/graph` | Observed nodes and relationships across the run’s recorded history |
| `GET /api/agent-runs/{id}/events?after=0` | Up to 200 durable activity events; resume with the last sequence number |
| `POST /api/agent-runs/{id}/reply` | Answer the current question |
| `POST /api/agent-runs/{id}/cancel` | Stop scheduling further work |

Reply body:

```json
{
  "revision": 4,
  "questionId": "id-from-current-run-question",
  "choice": "approve",
  "text": null
}
```

Use the actual offered choice IDs. A stale revision or already answered question returns `409`. Free text can be supplied instead of a choice for planning/clarification. A plan approval with extra text is treated as revision guidance. Completion is `status: completed` and an ordered list in `state.Clips`; it means every requested clip has a video artifact. Polling survives reconnects using the event cursor. No outbound notifications or webhooks are sent.

## Agctor calls Cynelo

The Cynelo checkout adds:

- `PUT /api/agctor/render/stories/{runId}` — idempotently create an empty owned story, with `{name}`. Reusing an ID with a different name returns `409`; no render is submitted.
- `GET /api/agctor/render/stories` — list only the configured integration owner’s stories.

- `PUT /api/agctor/render/jobs/{key}` — ensure a single render attempt.
- `POST /api/agctor/render/jobs/{key}/preflight` — validate the same input, film continuity and resolvable reference images without reserving a job or starting generation; returns `status: ready` and the reference selection. Already-submitted keys return `409`; inspect their existing job instead. Preflight is a point-in-time check, and submission validates again.
- `GET /api/agctor/render/jobs/{key}` — reconcile its progress and saved artifact.
- `GET /api/agctor/render/jobs/{key}/frame` — token-free continuation-frame access on the trusted connection.
- `GET /api/agctor/render/jobs/{key}/video` — token-free access to that job's saved clip on the trusted connection.

The key is `{runId}-{clipNumber}-{renderAttempt}`. The PUT carries run/workspace/story/shot IDs, expected Scene Memory revision, clip number/count, duration, prepared prompt and previous job key. Cynelo validates the owned approved shot and exact prompt. Both PUT and GET return `{id,status,videoUrl,lastFrameUrl,error}` with status `queued`, `running`, `completed` or `failed`. Artifact URLs use these job-scoped integration endpoints and need no credentials on localhost or an explicitly trusted network. They cannot read arbitrary media files.

Cynelo records a receipt before invoking generation. An identical retry reconciles the existing take; a conflicting payload returns `409`. On recovery, saved take/album records provide the output even when the in-memory job has disappeared. An interrupted submission with no provable outcome pauses for inspection instead of resubmitting automatically. A definitively failed render may create a new attempt within the configured limit. A network failure always retains the same attempt key.

Story links are available designs, not a mandatory cast list. The prepared shot includes only its participants and relevant physical continuity; unrelated linked characters, props and their images are excluded before Cynelo resolves references. The planner and interpreter receive only memory for identities named by the request, relevant physical continuity, and participants from earlier clips of this run. A new story does not inherit the previous story’s cast merely because it asks to keep lighting or characters consistent. Scoped validation rejects references to hidden existing identities. Repeated old facts and duplicate historical events do not create new shot participants. Full Scene Memory and archived take inputs remain unchanged.

Cynelo budgets at most eight reference images. When available images exceed that limit, it selects one per linked design first, then alternate views in stable round-robin order. All design descriptions remain in the approved prompt. More than eight designs with available images pauses rather than silently dropping a design. The receipt and job response retain the selected image IDs and counts (`references`), and Agctor persists and reports those counts in live activity. Retrying an accepted submission retains its original selection.

## Restart recovery

Recovery reconciles durable evidence, without requiring an LLM call. Agctor resumes its persisted run and stable render key; Cynelo checks the saved take, published album output, renderer queue and renderer history. AI planning remains separate from the decision to reuse or resubmit a paid render.

Cynelo records the exact workflow, client identity, output node and provider URL before submission, then durably binds the returned provider prompt ID. If the submit response is lost, the pre-recorded client ID plus exact workflow can identify the existing submission. Older takes without a provider ID use exact workflow equality and require a unique match across the queue and the latest 200 history entries. A known provider ID uses that job's specific history. Missing or ambiguous evidence never means permission to submit again.

| Verified state | Recovery action |
| --- | --- |
| Saved video exists for this take | Return its artifact immediately |
| Original provider job is queued or running | Reattach to it using the same take/job identity and resume progress/output monitoring |
| Original provider job completed | Download, validate and save its output without rerendering |
| Provider confirms failure | Report failure through the existing retry policy |
| Provider unreachable or evidence unavailable/ambiguous | Keep the current request key; report uncertainty and check again |
| Provider changed, film changed, or take explicitly cancelled | Block automatic recovery until the conflict is resolved; cancelled takes stay cancelled |

Recovery verifies the integration owner and the exact preceding clip before reattachment and again before saving. Cynelo persists explicit take cancellation. Concurrent status checks share the same recovery guard, so they do not launch multiple output watchers. Provider outages during a recovered watch retry observation without submitting new work. This is intended for one Cynelo process per workspace; it does not provide cross-process writer coordination.

When a status-retry question becomes obsolete because the original job is verified running again, Agctor clears that question and continues monitoring the same attempt with a refreshed wait deadline. Plan approvals, timeout decisions, explicit render retries and continuation questions retain their existing human controls. No LLM recovery adviser is implemented: uncertain identity must be resolved with provider evidence rather than model inference.

## Reliability and current limits

Run snapshots and ordered events commit together. Workers use renewable leases and revision checks; cancellation or a newer answer prevents an old worker from overwriting the run. Scene commands are checkpointed before commit and replayed by shot ID. External Scene Memory edits pause the workflow to avoid silently mixing continuity versions. Human waits and render polling release the worker between steps.

An already-submitted render continues to be checked during a timeout or render-status retry question, every 30 seconds by default (`WaitingPollSeconds`, bounded to 5–300 seconds). These server-side checks continue with the dashboard closed and resume after restart. They reconcile the existing job: Cynelo may reattach to its renderer and save its completed output, but these checks cannot submit a new render or automatically retry a failed one. The dashboard shows the last status check, connection failures and elapsed submission time; unchanged checks update freshness without adding duplicate activity events. Planning and review decisions are never polled as render jobs.

If a clip finishes during a human wait, its saved video becomes visible immediately. The last clip completes the run and clears the obsolete question; otherwise the question becomes “Continue with the next clip?” and requires an explicit answer. A failed render instead asks whether to retry. Concurrent answers and cancellation invalidate the observer's lease, so a late status response cannot overwrite a human decision. Normal polling also checks for completion before asking to extend a deadline.

Render responses optionally include `progress: { percent, stage, detail, updatedAt, estimated }`. Cynelo supplies a per-clip estimate, excluding earlier clips in the film; queued and preparation states have no percentage. The estimate includes generation and processing; it is not raw sampler progress or a prediction of time remaining. Unknown percentages remain null. In-flight values are capped at 99%, and only a validated saved video reports 100%. Agctor persists progress on each clip, displays a progress bar and stage details, and marks the last reading stale after a failed check. Both normal and human-wait polling record changed summaries rather than duplicate progress events. Older rendering bridges without progress remain compatible.

The first worker processes one transition at a time. It is intended for a lightweight, private sidecar with persistent local storage, not a distributed multi-region scheduler. HTTP calls and model calls have deadlines, failed operations have bounded retries, and render waits have a human-extendable deadline.

Scene Memory records planned story changes, not proof that a rendered video visually matches them. New runs dry-run their entire plan before approval, check three sampled frames per rendered clip through the configured vision model, and assemble accepted clips into a validated finished video. Sampled visual checks can miss motion and do not check audio. Budget accounting, selective invalidation/rerendering, and arbitrary model/tool selection remain future work. Cancelling Agctor stops further submissions but does not cancel an already submitted Cynelo/GPU job; use Cynelo to stop that job if needed.

Validation includes actor mailbox routing, multi-clip continuity, restart replay, lost render responses, idempotent admission, stale human replies, cancellation, external scene changes, HTTP integration and Cynelo's token-free two-clip rendering and connection-boundary tests. Rendering tests use the existing fake Comfy service rather than paid video generation.

## Production workflow (new runs)

The existing director mailbox routes durable transitions through Project agent → Planner → Scene Memory agent → Render agent → Continuity critic → Editor agent. Each transition remains bounded and checkpoints under the existing renewable lease and revision fence. No authoritative workflow state lives in actor instances. Existing persisted runs without `ProductionEnabled` retain their previous behavior rather than being silently migrated mid-render.

- **Projects:** `PUT sequences/{run}/project` links the owned story to a deterministic project identity, stored in the run. Cynelo renders in an isolated workspace and publishes immutable snapshots into its ordinary Projects catalog. Opening a snapshot does not redirect background work. Continuing an older story adopts a matching open film once; if old clips exist but the film is unavailable, it requests that film instead of silently losing continuity. One process per studio remains the supported writer topology.
- **Review:** Planning interprets all clips against a projected Scene Memory workspace without committing any shots. The UI shows action, duration, characters, props, locations, linked designs and available reference images. Approval freezes those interpretations; provisional entity IDs are resolved to committed identities when each shot is prepared. Changed Scene Memory still pauses execution. Up to eight available reference images are selected using Cynelo’s existing reference budget.
- **Critic:** `GET jobs/{key}/evidence` extracts beginning, middle and ending samples from the exact saved take, plus its preceding frame for story extensions. The vision model receives story intentions and these images, with a schema-validated result. Concerns, uncertainty or an unavailable model require an explicit **Keep this clip** or **Stop here** decision; free text is recorded without authorizing further rendering. There is no automatic quality-based rerender. Reviews remain in run state, and the UI offers **Watch this clip**.
- **Delivery:** After all clips are accepted, `PUT sequences/{run}/finish` verifies job ownership, order and the project tail, assembles the full story film, validates its duration, and publishes an immutable run-specific artifact. Retries use the same input hash and never start GPU work. Completed now means this final artifact exists. Cynelo provides authenticated preview/download and retains earlier finished exports after extensions.

Background project snapshots are immutable so an open editor remains usable when newer clips arrive; superseded snapshots are retained rather than deleting media another editor may still reference. Persistent storage should include both the workspace media and composer document store.

Cynelo-created runs now capture an optional `ProjectId` in their immutable input. The Project agent forwards it to Cynelo and rejects a different returned identity. Cynelo validates ownership and binds the run to that saved project, independent of subsequent editor switches. New projects have durable identities before starting a sequence. The user-facing sequence list is scoped to that project, including pending runs; continuing reuses its linked Scene Memory. Legacy dashboard requests without a project keep the previous automatic-project behavior, and existing runs retain their recorded bindings. Adopting an ordinary saved project uses independent copies of its existing media so catalog replacement cannot invalidate the background film.

Plan review checkpoints each validated clip in the run's projected workspace. Later previews retrieve participants from those checked clips, even before any real shot has been committed. A failed clip can be retried after an actor or service restart without reinterpreting earlier checked clips or submitting video. Explicit background music is interpreted as a Music entity (other sounds as Audio), and the model schema requires 1–20 participant references per event; the normal validation and correction pass still checks those references. Public failure messages distinguish invalid interpretations, timeouts and service errors without exposing model output or upstream response bodies. Regression coverage includes orphan music events and recovery at the second clip's review boundary.

### Audio Engineer

The scene-video conversation graph now includes **Director ↔ Audio Engineer**, mapped to the existing actor mailbox execution and persisted run transitions. The extra AgentLoop node declares `role: audio-engineer`; both request and report edges are required. Legacy three-node graphs remain valid. New runs opt into audio only when that specialist is present; previously persisted runs are unchanged.

After visual approval, `audio-plan` selects whether the prompt requests continuous music, `audio-process` requests an idempotent Cynelo draft, and `audio-review` inspects measured audio evidence and available local speech transcripts. `audio-decision` returns authority to the Director, which asks for an explicit original/improved choice and offers recheck. AI inspection is evidence-based; it does not claim direct listening, voice identification or lip-sync checking. Every transition checkpoints through the same leased actor mechanism. Speech notes without an explicit choice never approve a draft. Delivery cannot proceed without an audio choice for enabled runs.

Cynelo owns separated tracks, continuous-music mixing, bounded gain, ducking, normalization, before/after previews and immutable delivery. See Cynelo's `docs/agentic-sequences.md` for model setup, supported limits and separation limitations.

### Spoken-language review and separator selection

New Cynelo requests send `speechLanguage: "auto"` and `audioModel: "roformer"`. Auto follows explicit story speech intent; it does not assume English from an English-written prompt. Unclear intent pauses before planning for a language and optional exact dialogue. Explicit no-speech stories stay speech-free. Both fields are optional for compatibility: previously saved runs and older clients retain their earlier behavior. The audio specialist must be present in the conversation graph to enable speech review.

The Audio Engineer checks every completed clip independently of visual acceptance using Cynelo's local multilingual Whisper evidence. Confident matching-language transcripts also receive an LLM coherence/intent review. Language mismatch, uncertain words, unavailable inspection, or an inability to understand the transcript returns to the Director for human input. Model confidence is heuristic, not a guarantee of correctness. The review offers the current clip, transcript details, keep, recheck, stop, and up to two explicitly approved corrections per clip. Free text alone never authorizes a new render.

An approved correction creates a new render key with a `ReplacesJobKey` and separate `SpeechInstruction`; the canonical Scene Memory prompt remains unchanged. Cynelo verifies the replaced take belongs to the same workflow clip and is still the project tail. Replacement preserves the original album take, replaces that film slot, and keeps its predecessor for visual continuity. Durable replacement provenance also governs restart recovery; status checks never submit another render. Regeneration may change the visuals as well as the audio. It is not audio-only dubbing.

RoFormer is the default separator; Demucs is an explicitly selected legacy comparison backend. Model selection is frozen with the run, caches are separated by model/recipe, and missing models do not silently fall back. Reports retain model/runtime and processing time for future benchmarking. There is no automatic model recommendation or quality benchmark yet.

Verification (2026-11-08): 62 focused AGCTOR tests, 31 Cynelo integration/recovery tests, 18 Flutter Agentic tests, and 11 audio-worker tests passed. The macOS debug build passed. Static analysis reported three existing brace-style notices in the shared Cynelo API client. Offline English and Spanish speech fixtures retained their languages and words; silence produced no-speech. The live Cynelo speech endpoint inspected an existing completed clip and returned uncertainty. Both separator backends produced valid two-clip drafts with zero clipped samples and unchanged encoded video payloads. These are functional checks, not a perceptual-quality benchmark. No paid video jobs were submitted. Manual native click-through was blocked by duplicate running Cynelo app instances; new controls were covered by widget tests.

### Independent inspection and dedicated narration

Visual and speech reports are separate persisted checkpoints. An unavailable visual critic becomes an uncertain visual report; it never prevents speech inspection. Both results are gathered before human review, and accepting the visuals does not approve uncertain speech. Old visual-review waits receive one speech observation without consuming the pending approval. No provider audio stream is available: inspection runs once a clip is saved, not during frame decoding.

`narrationMode: "dedicated"` opts into off-screen English narration. Native/omitted mode retains video-generated speech. The Audio Engineer prepares one script per clip, and the creator must approve the exact words even when ordinary plan review is off. Other languages require an explicit English/native choice; auto never silently changes them. The render prompt suppresses native speech for dedicated narration.

Cynelo synthesizes the approved scripts using local Kokoro-82M with one fixed voice (`af_heart`), preserves narration per saved take across project continuations, and saves separate speech/music/sound-bed lanes. Speech verification checks English confidence and word error against the approved script, both before mixing and in the final mix. It allows bounded recognition variation; it is not a guarantee of perfect pronunciation. Two synthesis attempts are allowed per line, with a modest speed adjustment. Lines that still overrun are rejected, never truncated. Failed audio returns to human review without submitting video. Script/model/voice/timing/recipe and artifact checksums govern the cache.

A speech-review decision can switch an already-rendered clip to dedicated narration. The Director obtains a new script approval and retains the existing render key. Audio-review also offers narration revision, requiring another approval and changing audio only. The graph still uses Director ↔ Audio Engineer; every decision remains a leased actor transition. Cynelo owns model execution and media processing. English is the MVP language; no voice cloning, character lip sync, or automatic multilingual dubbing is claimed.

Narration verification (2026-11-08): 69 AGCTOR workflow/API/audio-engineer tests, 37 Cynelo integration/recovery tests, 22 Flutter tests, and 19 offline audio tests passed (147 focused tests). The audio-revision API tests were rerun after adding revision/persistence assertions. Flutter analysis and the macOS debug build passed. A real local two-clip mix matched both scripts before and after mixing, had zero clipped samples, and preserved the original encoded video SHA-256. The local services were restarted; the active run retained its provider job ID and render attempt. No new video generation was submitted for verification.

### Live agent activity graph

Cynelo's sequence status card opens **Agent activity**, a read-only modal. AGCTOR records explicit sender, recipient, phase and exchange IDs on workflow request/report events. Human questions and answers also have directions. Routes are stored transactionally in an additive SQLite table; older events remain readable with no guessed arrows. Exchange IDs are independent of work-lease credentials. All poll requests/reports are recorded, while the ordinary status view suppresses repetitive observations.

The modal displays the Director and specialist roles, animates newly received messages, and keeps the latest 100 events readable. It polls the existing authorized, cursor-paginated event API about once a second while open, reusing Cynelo's single serial refresh loop. Opening or reconnecting does not replay old history as live traffic. Pausing animation and reduced-motion settings preserve message updates. Closing the modal neither cancels the run nor changes approvals. Project changes clear the graph; connection failures and delayed updates are visible.

The graph now uses a left-to-right layout with typed agent, tool, LLM, memory and reported-model nodes. Connections come exclusively from observed routes; selecting an agent filters its neighbors. The topology endpoint groups persisted routes across the full run, so early model calls remain visible after recent poll messages leave the UI buffer. Cynelo applies the same ownership checks to graph and event reads.

Invocation-scoped tracing surrounds actual named-client HTTP calls and Scene Memory reads/writes. Ollama nodes use the exact model in each outgoing request, including repair calls. HTTP responses, failures and elapsed call times are paired with the request. These observations do not replace actor state, leases, approvals or recovery. Trace-storage failure cannot turn a successful external operation into a workflow retry. Prompts, credentials, endpoint URLs and response content are not copied into these diagnostic events.

Cynelo audio/speech responses can provide downstream model evidence. Those nodes are explicitly marked as reported evidence and never animated as new model invocations: the response may describe cached work. The graph does not claim token streaming, unseen internal provider traffic or retrospectively reconstruct tool calls from before tracing was enabled. Polling is approximately once a second for events and every five seconds for topology, only while the modal is open.


### Native H3 speech conditioning

Native speech stays the default; dedicated narration remains an explicit choice. Following the [official H3 prompt guide](https://huggingface.co/MiniMaxAI/MiniMax-H3/blob/main/docs/VIDEO_PROMPT_WRITING_GUIDE_base_en.md), the planner uses stable speaker IDs outside `<d>[English] ...</d>` blocks and preserves supplied spoken words. A deterministic fallback formats explicit quoted speech cues while leaving ordinary quoted titles/signs alone. Unsupported or ambiguous prose is left to planning rather than guessed as dialogue. Conflicting tagged languages, changes to explicitly quoted dialogue, or English speech exceeding the conservative two-words-per-second planning allowance request clarification before rendering; no words are silently shortened.

Each new plan records its native-speech formatting version. Legacy render requests retain their previous body for idempotent recovery. An explicitly requested speech correction uses a new render attempt, replaces the old tagged dialogue, and preserves the scene action. The existing two-correction limit and human choices remain authoritative. No new automatic paid render loop is introduced.

Recognized speech is compared with the requested words before the Audio Engineer's semantic review. Punctuation/case and apostrophe forms are normalized; missing, extra or changed words cause an uncertain result requiring a human decision. Speech recognition can be wrong, so a mismatch is not proof that the audio is bad. The dedicated narration checks remain separate.

Cynelo's saved-scene continuation passes the next scene's dialogue as current action, not already-completed history. Reference binding transforms names outside `<d>` blocks only, preserving the actual words inside the final H3 workflow. These changes improve conditioning and verification; they do not guarantee audio quality. Validate perceptual improvement by listening to fresh H3 renders with short explicit scripts.

### Separate human visual and audio decisions

Accepting a visual-review question sets a durable `AudioReviewRequired` flag for clips with speech review enabled. The same clip must then receive a distinct audio decision, even if automatic speech recognition previously passed. Rechecking speech refreshes evidence but cannot satisfy that human gate. Notes alone keep the run waiting, and question IDs/revisions continue protecting against stale replies. A final advancement guard prevents an unresolved audio decision from starting the next clip.

`SpeechVerified` describes the automated result; `SpeechAccepted` covers acceptance of original speech; `AudioDecision` records explicit `keep-original` or `replace-with-narration`. Dedicated narration does not set original speech to verified or accepted. After manual visual approval, a narration-mode run asks whether to replace the speech with the approved narration (final audio review remains later) review or revise its narration script, or recheck the original speech. These checks do not submit another render or change the audio mode of already accepted clips. Regeneration still requires its existing explicit choice and resets the changed clip's approvals.

The Cynelo review UI shows visual and audio status separately, labels playback as original audio, and displays expected words and the machine transcript in expandable details. Replacement narration is explicitly pending until production and verification finish. This change does not rewind clips that advanced before the update or approve any pending decisions for the user.


### Per-clip improved audio

Audio-enabled production runs now checkpoint `clip-audio-plan` → `clip-audio-process` → `clip-audio-check` → `clip-audio-decision` before advancing each clip. The Audio Engineer owns planning, preparation and evidence review; the Director requests explicit human approval. Notes alone never advance the run. Audio-only retry and narration revision preserve the video take. A failed/unavailable preview has no approve action.

Cynelo receives `preview: true` and the completed prefix of render keys through the existing audio endpoint. Each immutable draft has a `PreviewVersion`, a clip-only video and a lossless 48 kHz mix. AGCTOR checkpoints the preview and approved version on that clip. Subsequent previews and final assembly receive explicit `{ clipNumber, version }` approvals. Cynelo validates take identity, duration, soundtrack policy and narration against those versions before reusing them. A stale/foreign/missing approval is rejected. Approved narration remains fixed when later lines are revised.

The music bed uses the same absolute timeline as more clips arrive. Final assembly reuses approved PCM samples exactly; AAC export is lossy. Original clips remain separately playable. The final whole-film comparison remains available, including an explicit choice to use original audio instead. Old completed sequences are not retroactively changed. Existing in-progress runs pick up preview review at their next clip transition.


### In-flight work visibility

The run and graph APIs expose `activeCalls` containing operation, sender, recipient, start time and the current worker lease deadline. A separate SQLite projection records paired request starts/responses transactionally with diagnostic events. It survives history paging and reconnects; checkpoint completion, cancellation, lease expiry and replacement attempts remove the pending state. Lease tokens and request payloads are never exposed. This projection does not schedule work or change actor decisions.

Cynelo shows the deepest outstanding request in a compact status strip and highlights its chain in the graph, distinguishing waiting callers from the in-progress recipient. Elapsed time is not a completion estimate. Pending spinners respect animation pause and reduced motion; the text remains visible. Stale status stops the working indicators. During asynchronous rendering, the last confirmed provider state identifies queued/rendering work between polls, with a bounded freshness window.
