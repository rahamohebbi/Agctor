# PRD-020: Implementation tracking

**Status:** Done  
**Tracking owner:** AGCTOR maintainers  
**Last updated:** 2026-04-29

This document is the live implementation tracker for PRD-020. Keep product intent in `prd-020-agctor-prd.md`, implementation shape in `prd-020-implementation-plan.md`, and day-to-day progress here.

## Phase status

| Phase | Status | Requirements | Exit gate |
| --- | --- | --- | --- |
| Phase 1 — Message protocol and test foundation | Done | M1-M7, R1, R4 | Shared headers/builders in place; high-traffic actors and runtime adapters migrated; M7 regression tests added |
| Phase 2 — ProjectMemory actor workflow shell | Done | A1, A4, A5, M3 | Actor shell delegates to current pipeline with tests |
| Phase 3 — Actorized ProjectMemory workflow steps | Done | A2, A3, A4 | Extract, ingest, generic inbox, and query paths have actor boundaries; full workflow actor still provides compatibility orchestration |
| Phase 4 — Runtime adapter consistency | Done for current contract | R1-R7 | `InMemory` and `Proto.Actor` pass current conformance contract, including dead-letter behavior for missing send-only targets |
| Phase 5 — Agent behavior alignment | Done | G1-G4 | ProjectMemory C# extractor/query agents now use `IProjectMemoryLlmClient` and loaded YAML instructions |
| Phase 6 — Migration and cleanup | Done | A1-A5, M1-M5, R1-R5, G1-G4 | Full solution build and all test projects passed; `DotNetWorkspaceBuild` capped so hung `dotnet build` cannot block the suite |

## Requirement map

| ID | Requirement summary | Phase | Tracking status | Evidence |
| --- | --- | --- | --- | --- |
| A1 | Add ProjectMemory workflow actor/supervisor | Phase 2 | Done | `ProjectMemoryWorkflowActor` |
| A2 | Split workflow responsibilities into actor-owned steps | Phase 3 | Done | `ProjectMemoryExtractActor`, `ProjectMemoryIngestActor`, `ProjectMemoryGenericInboxActor`, `ProjectMemoryQueryActor` |
| A3 | Reuse existing ProjectMemory services inside actors | Phase 3 | Done | Actors delegate to `IProjectMemoryPipelineRunner` public APIs |
| A4 | Emit step results equivalent to current pipeline | Phase 2, Phase 3 | Done | Workflow actor returns `ProjectMemoryWorkflowResult` wrapping pipeline result |
| A5 | Keep compatibility path during migration | Phase 2, Phase 6 | Done | `ProjectMemoryPipelineExecutionMode.ActorWorkflow` is default; `Direct` remains available as an override |
| M1 | Add shared standard header names | Phase 1 | Done | `AgctorMessageHeaders` |
| M2 | Add envelope builder/helper APIs | Phase 1 | Done | `AgctorEnvelopeBuilder`, `MessageEnvelopeExtensions` |
| M3 | Add typed ProjectMemory workflow messages | Phase 2 | Done | `ProjectMemoryWorkflowMessages` |
| M4 | Keep string-header compatibility while new code uses builders | Phase 1+ | Done | Builders add existing header names; old code remains compatible |
| M5 | Preserve trace/activity propagation hooks | Phase 1, Phase 4 | Done | Builders preserve headers and use standard envelope shape |
| M6 | Migrate high-traffic actors/runtimes to shared protocol helpers | Phase 1+ | Done | `InMemory`/`Proto.Actor` runtime send + request/response paths and high-traffic actors now use shared header constants/builders |
| M7 | Add tests for missing/misspelled standard headers | Phase 1+ | Done | `AgctorEnvelopeBuilderTests` now includes typo/missing standard header regression cases |
| R1 | Add adapter-agnostic conformance tests | Phase 1, Phase 4 | Done | `InMemoryRuntimeConformanceTests` |
| R2 | Define missing actor behavior | Phase 4 | Done | Send-only emits `DeadLetter` and does not throw; request/response throws |
| R3 | Define acknowledgment vs final response behavior | Phase 4 | Done | Existing request-response behavior covered by conformance baseline |
| R4 | Define correlation propagation behavior | Phase 1, Phase 4 | Done | Builder and conformance tests cover correlation |
| R5 | Update runtime capability reporting | Phase 4 | Done | `ActorRuntimeDescriptor.Maturity` |
| R6 | Run conformance suite against Proto stable subset | Phase 4 | Done | `ProtoRuntimeConformanceTests` passes current contract |
| R7 | Keep runtimes experimental until required contract passes | Phase 4 | Done | `Proto.Actor` remains `experimental`; Orleans remains `placeholder` |
| G1 | Align C# ProjectMemory agents with YAML instructions | Phase 5 | Done | `PersonExtractorProjectAgent`, `PersonQueryProjectAgent` use loaded specs |
| G2 | Inject ProjectMemory agent dependencies | Phase 5 | Done | `IProjectMemoryAgentServices` adapter now backs extractor/query/curator agents; logic no longer directly depends on static service accessor calls |
| G3 | Share one ProjectMemory LLM client path | Phase 5 | Done | ProjectMemory C# extractor/query agents use `IProjectMemoryLlmClient` |
| G4 | Add YAML/C# contract drift tests | Phase 5 | Done | `ProjectMemoryAgentDriftTests` covers extractor/query prompt contract and curator service pipeline behavior |

## Implementation checklist

### Phase 1 — Message protocol and test foundation

- [x] Add `AgctorMessageHeaders` and standard message type constants under `AgctorSDK.Core/Messages/`.
- [x] Add envelope builder/helper APIs for command, request, response, acknowledgment, and error envelopes.
- [x] Add unit tests for helper defaults, correlation propagation, and backward-compatible header reads.
- [x] Add first adapter conformance tests for `InMemory` spawn, send, request/response, and correlation.
- [x] Record missing-actor behavior decision before changing runtime behavior.
- [x] Migrate high-traffic runtime/actor standard headers to shared constants/builders.
- [x] Add explicit regression tests for misspelled/missing framework-owned headers.

### Phase 2 — ProjectMemory actor workflow shell

- [x] Add workflow request/result/error message models.
- [x] Add `ProjectMemoryWorkflowActor` under `AgctorSDK.Core/ProjectMemory/Orchestration/Actors/`.
- [x] Delegate internally to `IProjectMemoryPipelineRunner` for first parity slice.
- [x] Add tests that compare actor shell result to direct pipeline result.
- [x] Add configuration option for direct pipeline vs actor workflow.

### Phase 3 — Actorized ProjectMemory workflow steps

- [x] Add extractor actor or actor-owned handler.
- [x] Add ingest/curation actor or actor-owned handler.
- [x] Add generic inbox confirmation actor or actor-owned handler.
- [x] Add query actor or actor-owned handler.
- [x] Keep parsing, routing, projection, generic inbox, and LLM services as business-rule owners.
- [x] Expand parity tests across ingest-only, query-only, auto mode, parse failure, route miss, route miss confirmation, and generic inbox persistence.

### Phase 4 — Runtime adapter consistency

- [x] Apply documented missing-actor policy consistently for `InMemory` baseline.
- [x] Ensure acknowledgments do not complete final-response requests unless expected.
- [x] Ensure error envelopes preserve correlation id.
- [x] Use shared envelope builders inside runtime adapters where practical.
- [x] Update `ActorRuntimeCatalog` with supported/experimental/placeholder capability labels.
- [x] Extend conformance tests to `Proto.Actor` where stable.
- [x] Track known Proto drift until send-only envelope construction, missing actor behavior, and correlation match the required contract.

### Phase 5 — Agent behavior alignment

- [x] Route ProjectMemory C# agents through loaded `AgentDefinitionSpec` instructions by default.
- [x] Replace direct static ProjectMemory service access with injected dependencies or a focused factory adapter.
- [x] Route Host persona runner and ProjectMemory agents through a shared LLM client abstraction where practical for C# agents.
- [x] Add drift tests for `person-extractor`, `memory-curator`, and `person-query`.
- [x] Reduce reflection-heavy spawning paths if a focused helper can do so safely.

### Phase 6 — Migration and cleanup

- [x] Flip default ProjectMemory execution to actor workflow after broader parity coverage.
- [x] Convert `ProjectMemoryPipelineRunner` into a facade or isolate its reusable pure services.
- [x] Keep temporary fallback logging for direct pipeline mode.
- [x] Update architecture docs and diagrams if module boundaries changed.
- [x] Run full build, unit tests, and integration tests before marking done.

## Verification gates

| Gate | Command or evidence | Status | Notes |
| --- | --- | --- | --- |
| Docs tracker created | `Project/prd-020/prd-020-tracking.md` exists and is linked | Done | |
| Phase 1 build | Build all projects | Done | `dotnet build Agctor.sln` passed with existing warnings (final verification) |
| Phase 1 unit tests | Message/helper and runtime tests | Done | Targeted PRD-020 filter passed 21/21 |
| Phase 2 parity tests | Actor shell vs direct pipeline | Done | Actor-backed facade tests passed |
| Phase 3 parity tests | Actorized steps vs direct pipeline | Done | Updated targeted PRD-020 filter passed 23/23 |
| Phase 4 conformance tests | Runtime adapter matrix | Done | Runtime conformance filter passed 9/9 after dead-letter consistency |
| Proto stable subset | Runtime adapter matrix | Done | Proto passes current stable subset including dead-letter missing actor behavior |
| Agent protocol cleanup slice | Focused agent tests | Done | `dotnet test AgctorSDK.Core.Tests/AgctorSDK.Core.Tests.csproj --filter "FullyQualifiedName~AgentFactoryTests|FullyQualifiedName~TaskScoperAgentTests|FullyQualifiedName~LLMAgentTests"` passed 9/9 after `Agent.cs` header-constant migration |
| High-traffic agent header-key cleanup | Focused agent tests | Done | `CoderAgent`, `LLMAgent`, and `SessionCoordinatorAgent` now use `AgctorMessageHeaders`/`AgctorMessageTypes` for standard protocol keys; targeted filter passed 7/7 |
| Remaining agent header sweep | Focused agent tests | Done | `AgentFactory`, `SessionMemoryAgent`, and ProjectMemory person agents now use shared standard header constants; targeted filter passed 118/118 |
| Header typo/missing regression tests | Message + conformance tests | Done | `dotnet test AgctorSDK.Core.Tests/AgctorSDK.Core.Tests.csproj --filter "FullyQualifiedName~AgctorEnvelopeBuilderTests|FullyQualifiedName~InMemoryRuntimeConformanceTests|FullyQualifiedName~ProtoRuntimeConformanceTests"` passed 16/16 with new typo/missing header guards |
| Phase 3 parity expansion | Workflow actor parity tests | Done | `ProjectMemoryWorkflowActorTests.ActorBackedRunner_Parity_Covers_Phase3_Scenarios` now covers ingest-only, query-only, auto mode, parse failure, route miss, route miss confirmation, and generic inbox persistence; workflow actor test filter passed 9/9 |
| Runtime adapter builder sweep | Runtime conformance + contract tests | Done | `InMemoryActorRuntime` and `ProtoActorAdapter` response/reply paths now use shared protocol constants/builders; runtime conformance filter passed 25/25 |
| Phase 5 drift tests | YAML/C# agent contract tests | Done | `ProjectMemoryAgentDriftTests` passed for `person-extractor`, `memory-curator`, and `person-query`; focused filter passed 15/15 |
| Phase 5 dependency adapter | ProjectMemory C# agent dependency seam | Done | `ProjectMemoryAgentServices` introduced and wired into extractor/query/curator agents |
| Phase 6 service isolation slice | ProjectMemory orchestration modularity | Done | Added `ProjectMemoryQueryService` and routed query orchestration through it; ProjectMemory test filter passed 27/27 |
| Phase 6 ingest orchestration isolation | ProjectMemory orchestration modularity | Done | Added `ProjectMemoryIngestService` and routed ingest + confirmation orchestration through it; ProjectMemory test filter passed 27/27 |
| Final verification run | Build + unit + integration | Done | `dotnet build Agctor.sln` succeeded; `AgctorSDK.Core.Tests` 371/371; `AgctorSDK.CodeGraph.Tests` 48/48; `AgctorSDK.Core.IntegrationTests` 31/31; `AgctorSDK.Host.IntegrationTests` 115/115 |
| Final integration tests | Host/Core integration tests | Done | Full `AgctorSDK.Core.IntegrationTests` and `AgctorSDK.Host.IntegrationTests` runs (no filter) as part of final verification |

## Decision log

| Date | Decision | Reason | Status |
| --- | --- | --- | --- |
| 2026-04-26 | Keep `ProjectMemoryPipelineRunner` as compatibility path until actor parity is proven. | Avoid changing file output while moving orchestration boundaries. | Accepted |
| 2026-04-26 | Treat `InMemory` as the required runtime conformance baseline. | It is the current reliable MVP runtime and local test default. | Accepted |
| 2026-04-26 | Treat `Proto.Actor` as supported only for capabilities that pass conformance tests. | Prevent dashboards or callers from assuming behavior that has not been proven. | Accepted |
| 2026-04-26 | New actor/protocol code should use shared message builders. | Avoid further spread of hand-built protocol headers. | Accepted |
| 2026-04-26 | Preserve `InMemory` send-only missing actor behavior for compatibility. | Existing tests and callers expect no throw; request/response still throws for missing actors. | Accepted |
| 2026-04-26 | Actor workflow is the default ProjectMemory execution mode. | The framework should use the Actor Model by default; direct mode stays available as a compatibility override. | Accepted |
| 2026-04-27 | Loose message cleanup and Proto conformance remain active PRD-020 work. | Shared helpers exist, but runtime/high-traffic actor migration and multi-runtime contract tests are not complete. | Accepted |
| 2026-04-27 | Proto passes the stable conformance subset but remains experimental. | Send-only standard envelopes and request/response pass; missing-actor policy still differs from InMemory and is tracked with a skipped conformance test. | Accepted |
| 2026-04-27 | Send-only missing actors emit `DeadLetter` consistently across `InMemory` and `Proto.Actor`. | Fire-and-forget sends remain compatibility-friendly while becoming observable. | Accepted |
| 2026-04-28 | Migrate base `Agent` protocol header usage to shared constants before broader actor sweep. | `Agent.cs` is a high-traffic path and had many hand-written standard header keys that increase typo risk. | Accepted |
| 2026-04-28 | Apply the same standard-header cleanup to other high-traffic agents. | `CoderAgent`, `LLMAgent`, and `SessionCoordinatorAgent` had repeated framework-owned key literals that increase drift/typo risk. | Accepted |
| 2026-04-28 | Complete remaining `AgctorSDK.Agents/Agents` standard-header key sweep. | `AgentFactory`, `SessionMemoryAgent`, and ProjectMemory person agents still had framework-owned protocol literals. | Accepted |
| 2026-04-28 | Enforce typo/missing standard-header guards with explicit unit tests. | M7 requires compiler-independent regression protection where protocol keys are dictionary-based. | Accepted |
| 2026-04-28 | Expand actor-backed parity tests before closing Phase 3 checklist coverage. | Existing tests validated core actor wiring but did not explicitly cover all listed ingest/query/auto and route-miss scenarios. | Accepted |
| 2026-04-28 | Complete runtime adapter protocol helper sweep for request/response/reply paths. | Phase 4 required practical builder/constant use inside runtime adapters, including error/reply envelopes in processing loops. | Accepted |
| 2026-04-29 | Introduce a focused ProjectMemory agent dependency adapter and inject it into C# agents. | Improves testability/maintainability while preserving runtime behavior through a default adapter implementation. | Accepted |
| 2026-04-29 | Centralize AgentFactory reflection-based spawn into one helper. | Reduces duplicated fragile reflection logic and makes runtime-spawn behavior easier to maintain. | Accepted |
| 2026-04-29 | Start Phase 6 by extracting query orchestration into a reusable service. | Reduces `ProjectMemoryPipelineRunner` responsibility and enables cleaner facade-style composition over time. | Accepted |
| 2026-04-29 | Continue Phase 6 by extracting ingest + confirmation orchestration into a reusable service. | Further reduces `ProjectMemoryPipelineRunner` surface area and isolates high-change ingest/confirmation business flow for easier testing and future actor reuse. | Accepted |
| 2026-04-29 | Cap `DotNetWorkspaceBuild.BuildAsync` with a cancellable timeout and kill the process tree on expiry. | Prevents hung `dotnet build` / NuGet restore from blocking the full unit test host; closes final PRD-020 verification gate. | Accepted |

## Risk log

| Risk | Impact | Mitigation | Status |
| --- | --- | --- | --- |
| Actor workflow changes existing ProjectMemory file output | High | Start with delegating shell and parity tests before replacing steps | Mitigated |
| Refactor scope grows too large | High | Ship phase-by-phase and keep fallback until Phase 6 | Mitigated |
| Proto conformance gaps block progress | Medium | Mark gaps as experimental; do not block `InMemory` hardening | Mitigated for current contract |
| Message helpers become too abstract | Medium | Keep helpers focused on standard protocol fields only | Open |
| Constructor injection conflicts with actor spawning | Medium | Add focused factories/adapters before broad runtime changes | Open |
