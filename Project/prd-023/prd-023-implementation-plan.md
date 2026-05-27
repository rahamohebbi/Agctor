# PRD-023 — Implementation plan

Phased delivery. Each phase should compile and have targeted tests before the next. Follow workspace rule: after major phases, **build all projects**, run **unit** then **integration** tests.

## Phase 023a — Blob store + asset catalog ✅ (initial)

| Step | Work | Status |
| --- | --- | --- |
| 1 | `IBlobStore` + `S3CompatibleBlobStore` + `FileSystemBlobStore` | Done |
| 2 | `VisualStorageOptions` + `AddAgctorVisualMemory` | Done |
| 3 | Asset YAML `VisualAssetCatalogStore` | Done |
| 4 | `VisualAssetSupervisorActor` + `ActorBackedVisualAssetUploadService` | Done |
| 5 | `VisualAssetsController` — init/complete/list/get + raw PUT (file provider) | Done |
| 6 | Host README + `appsettings.json` (`Provider: file` default) | Done |

**Tests:** `VisualAssetCatalogStoreTests`, `VisualAssetSupervisorActorTests`.

## Phase 023b — Chat turns + playground UX (see UX spec) ✅ (initial)

| Step | Work | Status |
| --- | --- | --- |
| 1 | `SessionTurn.AttachmentsJson` + SQLite migration in `SqliteSessionStore` | Done |
| 2 | Extend `ProjectMemoryPlaygroundStreamRequestDto` (`turnGroupId`, `attachments` — `uploaded` OK without subjects) | Done |
| 3 | `AppendPlaygroundTurnAsync` persists attachment manifest | Done |
| 4 | Transcript API returns attachments + signed `viewUrl` | Done |
| 5 | **UX:** composer chips, drag/drop/paste, Send without tags ([prd-023-ux-spec.md](./prd-023-ux-spec.md)) | Done |
| 6 | **UX:** user bubble inline images + footer states (`Analyzing…` / `Insights ready`) | Done (Analyzing… on send; full extract in 023d) |
| 7 | Optional Tag popover (collapsed default); SSE wiring | Deferred |
| 8 | SSE: `attachment_state`, `attachment_preview` | Done |

**Tests:** Session store round-trip; stream with attachment-only body (no text); UX smoke checklist §12 in UX spec.

## Phase 023c — Visual tools ✅ (initial)

| Step | Work | Status |
| --- | --- | --- |
| 1 | `PersonVisualIngestTool` — all ingest operations | Done |
| 2 | `PersonVisualContextTool` — `BuildContext`, `RetrieveByIntent`, `ListForEntity` | Done |
| 3 | `PersonVisualExtractTool` — stub `Extract` (no Ollama yet) | Done |
| 4 | `[AgctorHostTool]` + `ToolActorDiscovery` parameter hints | Done |
| 5 | `ProjectMemoryPersonaToolRouting` + `ScenarioFlowLlmNodeToolIds` | Done |
| 6 | Refactor upload HTTP to invoke ingest tool | Done |

**Tests:** Tool unit tests with temp project + mock blob store.

## Phase 023d — Ollama Gemma 4 vision ✅ (initial)

| Step | Work | Status |
| --- | --- | --- |
| 1 | `IOllamaVisionChatClient` + `OllamaVisionChatClient` (`/api/chat`, `images[]`) | Done |
| 2 | `OllamaRuntimeConfiguration` — `VisionModel`, fallbacks, timeout | Done |
| 3 | `VisualIngestActor` — thumbnail, EXIF | Done (captured-at + byte count; thumbnail deferred) |
| 4 | `VisualInferActor` or extract pre-pass — `InferFromPrompt` (message, focus, entities) | Done |
| 5 | `VisualExtractActor` — S3 download, resize, extract prompt v1, parse JSON (uses inferred subjects) | Done |
| 6 | Wire extract → `IGenericInboxStore` proposals | Done |
| 7 | Host startup health: vision model present (`gemma4:31b`) | Done |
| 8 | Implement `PersonVisualExtractTool.Extract` + `InferFromPrompt` | Done |

**Tests:** Parser tests with fixture JSON; extract actor with mock vision client; optional live test `[Explicit]` if Ollama + gemma4 available.

**Prompt:** `visual-extract-v1` — system: JSON-only memoryIntents; user: image before text; no `<|think|>`.

## Phase 023e — Scenario flow + personas ✅

| Step | Work | Status |
| --- | --- | --- |
| 1 | Agent YAML: `visual-intake`, `style-coach`, `fitness-coach` under `samples/people-project/.agctor/agents/` | Done |
| 2 | Update `person-query`, `relationship-coach` tool allows | Done |
| 3 | Extend `people` flow in `agctor-scenarios.user.json` (nodes, edges, router hints) | Done |
| 4 | `PlaygroundFlowPreRouter` (deterministic) before LLM router | Done |
| 5 | `routingContext` builder from attachments + project focus | Done |
| 6 | Host step `pm-visual-extract` between extractor and curator | Done |
| 7 | `PlaygroundPersonQueryContextBuilder` — load visual context when `toolIds` present | Done |
| 8 | Multimodal persona call: pass images to vision client when turn has attachments | Done |

**Tests:** Pre-router matrix tests; flow validator still passes; integration: stream with attachment mock.

## Phase 023f — Privacy + session end ✅

| Step | Work | Status |
| --- | --- | --- |
| 1 | `VisualPrivacyActor` — delete S3 keys by prefix / asset index | Done |
| 2 | Extend `PrivacyMemoryService.ForgetPersonAsync` | Done |
| 3 | Export zip includes `visual/` tree | Done |
| 4 | Extend `SessionEndIngestActor` for attachment manifests | Done |
| 5 | Playground: visual inbox badge + inbox row thumbnails; clarify chips (low confidence) | Done |

**Tests:** `PrivacyMemoryServiceTests` visual purge; forget removes asset yaml.

## File map (expected)

| Area | Location |
| --- | --- |
| Blob + options | `AgctorSDK.Core/ProjectMemory/Visual/` |
| Vision Ollama client | `AgctorSDK.Core/Ollama/OllamaVisionChatClient.cs` |
| Actors | `AgctorSDK.Core/ProjectMemory/Visual/Actors/` |
| Tools | `AgctorSDK.Tools/Tools/Implementations/PersonVisual*.cs` |
| HTTP | `AgctorSDK.Host/Controllers/VisualAssetsController.cs` (or extend `ProjectMemoryController`) |
| Stream | `ProjectMemoryController.PlaygroundMessageStream` |
| Pre-router | `AgctorSDK.Host/Services/ProjectMemory/PlaygroundFlowPreRouter.cs` |
| Playground UI | `wwwroot/js/dashboard/project-memory-playground.js`, `Playground.cshtml` |
| Agent YAML | `samples/people-project/.agctor/agents/people/*.agent.yaml` |
| Scenario flow | `AgctorSDK.Host/Config/agctor-scenarios.user.json` |
| DI | `CompanionMemoryServiceExtensions`, `HostWebServiceExtensions` |

## Test matrix

| Test | Project |
| --- | --- |
| `PersonVisualIngestToolTests` | Core.Tests |
| `PersonVisualContextToolTests` | Core.Tests |
| `VisualExtractActorTests` (mock vision) | Core.Tests |
| `PlaygroundFlowPreRouterTests` | Core.Tests |
| `PrivacyMemoryServiceVisualPurgeTests` | Core.Tests |
| Playground stream + attachment | Host.IntegrationTests |

## Documentation (post-implementation)

Update when code lands:

- `AgctorSDK.Host/README.md` — `VisionModel`, MinIO, `ollama pull gemma4:31b`
- `samples/people-project` — example `visual/assets` layout
- Optional: `Project/prd-023/docs/visual-chat-sequence.mmd` (sequence diagram)

## Out of scope reminder

Calendar/contacts remain deferred (not PRD-023). PRD-022 implementation plan line “PRD-023+” for calendar should be read as **future PRD-024+** once calendar is scheduled.
