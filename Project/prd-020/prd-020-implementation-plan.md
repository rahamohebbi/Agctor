# PRD-020: Implementation plan — Actor-first maintainability and runtime consistency

**Status:** Planned.

Live progress is tracked in [prd-020-tracking.md](./prd-020-tracking.md). Keep this file focused on the intended implementation shape; update phase status, evidence, decisions, risks, and verification results in the tracker.

Follow workspace rules: after a major feature slice, build all projects, run unit tests, then run integration tests.

## Delivery checklist

| Area | Target artifacts |
| --- | --- |
| Phase 1 — protocol foundation | Shared message header constants, envelope builders, adapter conformance test harness |
| Phase 2 — ProjectMemory actor workflow shell | Workflow actor messages, supervisor actor, compatibility facade, parity tests |
| Phase 3 — actorized workflow steps | Extract, ingest/curate, generic inbox confirmation, query actors or actor-owned handlers |
| Phase 4 — runtime adapter consistency | Shared envelope behavior, missing actor policy, capability reporting, conformance tests |
| Phase 5 — agent behavior alignment | Injected ProjectMemory agent dependencies, unified LLM client path, YAML/C# contract drift tests |
| Phase 6 — migration and cleanup | Make actor workflow canonical, retire duplicated behavior, update docs |

## Phase 1 — Message protocol and test foundation

**Objective:** Make new actor code harder to break by replacing repeated string headers with shared protocol helpers.

1. **Message constants and helpers.**
   - Add standard header names and message type constants under `AgctorSDK.Core/Messages/`.
   - Add envelope builder APIs for command, request, response, acknowledgment, and error envelopes.
   - Add helper methods for reading/writing correlation ids and sender/receiver values.
2. **Runtime-neutral test harness.**
   - Create adapter conformance test fixtures in `AgctorSDK.Core.Tests/Runtime/` or `AgctorSDK.Core.IntegrationTests/Runtime/`.
   - Start with `InMemory` as required.
   - Add `Proto.Actor` to the same matrix where stable.
3. **Migration guardrails.**
   - New code should use builders.
   - Existing code can remain until touched.
   - High-traffic runtime and actor paths should be migrated from repeated header literals to the shared constants/builders as follow-up slices.

**Exit:** New helper APIs are covered by unit tests; conformance harness can validate at least spawn, send, request/response, and correlation for `InMemory`.

## Phase 2 — ProjectMemory actor workflow shell

**Objective:** Add an actor boundary around ProjectMemory orchestration without changing behavior.

1. **Messages.**
   - `ProjectMemoryWorkflowRequest`
   - `ProjectMemoryWorkflowResult`
   - `ProjectMemoryStepCompleted`
   - `ProjectMemoryWorkflowFailed`
2. **Actor shell.**
   - Add a supervisor/workflow actor under `AgctorSDK.Core/ProjectMemory/Orchestration/Actors/`.
   - Initially delegate to `IProjectMemoryPipelineRunner` internally so behavior is identical.
   - Return the same result shape the current runner returns.
3. **Facade option.**
   - Add an option that lets Host choose direct pipeline or actor workflow.
   - Default to direct pipeline until Phase 3 parity is complete.

**Exit:** Host/tests can execute ProjectMemory through the actor workflow shell with no behavior change.

## Phase 3 — Actorized ProjectMemory workflow steps

**Objective:** Move orchestration decisions into actor messages while keeping existing service classes as the business-rule owners.

1. **Extractor actor.**
   - Receives extract request.
   - Loads the relevant `AgentDefinitionSpec`.
   - Calls injected `IProjectMemoryLlmClient`.
   - Returns raw extractor text plus trace-ready detail.
2. **Ingest/curator actor.**
   - Receives raw extractor output.
   - Calls existing parse, route, projection, resolution mention publishing, and generic inbox services.
   - Returns parse source, updated files, route issues, and out-of-schema proposals.
3. **Generic inbox confirmation actor or handler.**
   - Receives confirmation signal and pending approvals.
   - Calls `IGenericInboxStore`.
   - Returns persistence result.
4. **Query actor.**
   - Receives query request and conversation context.
   - Calls existing query prompt/LLM logic.
   - Applies resolution annotation where available.
5. **Workflow supervisor.**
   - Coordinates actors and assembles final `ProjectMemoryPipelineResult`.
   - Handles failure rules equivalent to today’s auto/ingest-only/query-only modes.

**Exit:** Actor workflow passes parity tests for ingest-only, query-only, auto mode, parse failure, route miss, out-of-schema prompt, and confirmation follow-up.

## Phase 4 — Runtime adapter consistency

**Objective:** Make runtime selection trustworthy.

1. **Missing actor policy.**
   - Decide and document whether send-only missing actor is dropped, returns a result, or raises a dead-letter event.
   - Apply consistently in `InMemory` and supported adapters.
2. **Request/response policy.**
   - Ensure acknowledgments do not complete final-response requests unless explicitly expected.
   - Ensure error envelopes preserve correlation id.
3. **Shared construction.**
   - Use shared envelope builders in runtime adapters where practical.
4. **Capability reporting.**
   - Update runtime catalog descriptors to distinguish supported, experimental, and placeholder behavior.
5. **Proto subset.**
   - Add Proto.Actor conformance tests for the stable subset.
   - Keep known Proto drift visible with skipped or failing-follow-up test cases in the tracker rather than silently accepting behavior differences.

**Exit:** Adapter conformance tests document and enforce the common behavior. Dashboard copy reflects actual runtime maturity.

## Phase 5 — Agent behavior alignment

**Objective:** Remove drift between YAML agent definitions, C# agents, pipeline prompts, and Host persona calls.

1. **Dependency injection.**
   - Replace static `ProjectMemoryServiceAccessor` usage in ProjectMemory C# agents with injected dependencies where actor spawning supports it.
   - If constructor injection cannot be done everywhere yet, add an adapter/factory pattern rather than direct static access in each agent.
2. **Unified LLM client.**
   - Route Host persona runner and ProjectMemory agents through the same LLM client abstraction as the pipeline where possible.
3. **Prompt contract consistency.**
   - Make C# ProjectMemory agents use loaded YAML instructions by default.
   - Add tests that compare key instruction constraints for `person-extractor` behavior.
4. **Agent factory cleanup.**
   - Reduce reflection-heavy spawning paths where possible with typed helper APIs or runtime factory overloads.

**Exit:** `person-extractor`, `memory-curator`, and `person-query` have one clear source of behavior, and test failures catch accidental prompt/contract divergence.

## Phase 6 — Migration and cleanup

1. **Make actor workflow canonical.**
   - Flip Host/project option so ProjectMemory uses actor workflow by default after parity.
   - Keep a temporary compatibility fallback with logging.
2. **Retire duplication.**
   - Convert `ProjectMemoryPipelineRunner` into a facade over the actor workflow, or split reusable pure services from the old orchestration path.
3. **Documentation.**
   - Update Core README and PRD docs with canonical actor workflow.
   - Add or update architecture diagrams when code changes alter module boundaries.
4. **Hardening.**
   - Add integration tests for Host path and runtime selection.
   - Add chaos-style tests for actor shutdown/restart mid-workflow if practical.

## Test plan

| Test area | Coverage |
| --- | --- |
| Unit | Envelope builders, typed message helpers, missing-header behavior, ProjectMemory workflow messages |
| Core tests | Pipeline parity for actor workflow, generic inbox confirmation, out-of-schema proposals |
| Runtime tests | Adapter conformance matrix for spawn, send, request/response, correlation, timeout, missing actor |
| Integration tests | Host ProjectMemory flow through actor workflow, trace step parity, runtime selection |
| Regression tests | YAML/C# agent prompt contract drift, persona runner LLM client behavior |

## Remaining hardening focus

1. **Loose message cleanup:** migrate standard protocol strings in runtimes and high-traffic actors to `AgctorMessageHeaders`, `AgctorMessageTypes`, and `AgctorEnvelopeBuilder`.
2. **Runtime conformance matrix:** keep `InMemory` as the required baseline; add Proto.Actor stable-subset tests; do not mark Proto as supported until the common contract passes.

## Risks

| Risk | Mitigation |
| --- | --- |
| Actor workflow changes file output | Start with delegating shell, then add parity tests before replacing steps. |
| Too much refactor at once | Ship phases independently; keep the direct pipeline fallback until Phase 6. |
| Runtime conformance exposes Proto gaps | Mark capabilities honestly; do not block InMemory hardening on Proto completeness. |
| Message helpers become another abstraction layer | Keep helpers small and focused on common protocol fields only. |
| C# agent constructor injection conflicts with current runtime spawning | Add factory overloads or adapter classes rather than forcing a broad runtime rewrite. |

## Proposed module placement

| Area | Project / path |
| --- | --- |
| Message protocol helpers | `AgctorSDK.Core/Messages/` |
| Runtime conformance tests | `AgctorSDK.Core.Tests/Runtime/`, `AgctorSDK.Core.IntegrationTests/Runtime/` |
| ProjectMemory workflow actors/messages | `AgctorSDK.Core/ProjectMemory/Orchestration/` |
| ProjectMemory agent alignment | `AgctorSDK.Agents/Agents/ProjectMemory/` |
| Host wiring | `AgctorSDK.Host/Program.cs`, `AgctorSDK.Host/Services/ProjectMemory/` |
| Docs | `Project/prd-020/`, project `docs/` folders if architecture diagrams change |

