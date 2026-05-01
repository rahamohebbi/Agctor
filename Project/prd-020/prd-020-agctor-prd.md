# PRD-020: Actor-first maintainability and runtime consistency

## 1. Overview

AGCTOR’s long-term maintainability depends on one simple expectation: if a feature is part of the agentic framework, contributors should know how it moves through actors, messages, runtimes, and agents without reading half the repository.

Today the codebase has strong actor-model pieces, especially ProjectMemory resolution, but the architecture scan found three high-priority risks:

1. The main ProjectMemory ingest/query workflow is still mostly a synchronous service pipeline.
2. Actor communication uses a mix of typed messages, string headers, and raw payload conventions.
3. Runtime adapters duplicate envelope and correlation behavior, so `InMemory` and `Proto.Actor` can drift.

PRD-020 hardens those boundaries without changing user-visible product behavior first.

## 2. Goals

### 2.1 Actor-first ProjectMemory orchestration

1. Introduce an actor-owned ProjectMemory workflow that coordinates extraction, ingest/curation, generic inbox confirmation, and query through actor messages.
2. Preserve the current `ProjectMemoryPipelineRunner` behavior during migration, including file effects, trace output, scenario scoping, and out-of-schema behavior.
3. Make the actor workflow the canonical path once parity is proven.

### 2.2 Shared message protocol

1. Define shared message header names, message types, correlation helpers, and envelope builders in Core.
2. Reduce hand-built dictionaries for `MessageType`, `SenderId`, `ReceiverId`, `CorrelationId`, and reply metadata.
3. Prefer typed message payloads for framework protocols while still allowing opaque payloads for extensibility.

### 2.3 Runtime adapter consistency

1. Define adapter conformance tests for spawn, send, request/response, missing actor behavior, correlation propagation, acknowledgments, shutdown, and statistics.
2. Share common envelope creation and request/response conventions between runtime adapters.
3. Clearly label unsupported or placeholder runtime capabilities so dashboards and callers do not imply production support where it does not exist.

### 2.4 Agent behavior consistency

1. Align YAML-defined agent behavior, C# project agents, pipeline prompts, and Host persona runners.
2. Use injected services for LLM calls and ProjectMemory dependencies rather than static service access where possible.
3. Keep agent role definitions portable and testable.

## 3. Non-goals

1. Rewriting every actor in the repository in one milestone.
2. Replacing the existing ProjectMemory pipeline before actor parity tests pass.
3. Changing ProjectMemory storage formats or markdown output as part of this architecture work.
4. Making Orleans production-ready in this PRD. Orleans capability reporting may be clarified, but implementation is separate.
5. Introducing a new UI feature unless needed for diagnostics or parity validation.

## 4. Current behavior

- `ProjectMemoryPipelineRunner` is the main ingest/query workflow and explicitly describes itself as a code-first orchestrator without actor envelopes.
- ProjectMemory resolution already uses an actor topology: supervisor, reconciler, mention index, and per-entity resolution actors.
- `MessageEnvelope` carries an `object` payload, mutable metadata, and string headers.
- Runtime adapters build and interpret envelopes independently.
- ProjectMemory C# agents load YAML specs but also contain behavior and prompt logic that can drift from Host persona and pipeline flows.

## 5. Requirements

### 5.1 ProjectMemory actor workflow

| ID | Requirement |
| --- | --- |
| A1 | Add a `ProjectMemoryWorkflowActor` or similarly named supervisor actor that receives a user turn and coordinates the ProjectMemory workflow. |
| A2 | Split workflow responsibilities into small actors or actor-owned handlers: extraction, ingest/curation, generic inbox confirmation, and query. |
| A3 | Reuse existing services for parsing, routing, projection, generic inbox, resolution, and LLM calls; do not duplicate business rules inside actors. |
| A4 | Actor workflow must emit step results equivalent to current `ProjectMemoryPipelineStep` records so existing trace and test expectations can be preserved. |
| A5 | Keep a compatibility path: callers can use the existing pipeline while actor workflow parity is being built and tested. |

### 5.2 Message protocol

| ID | Requirement |
| --- | --- |
| M1 | Add shared constants or value objects for standard headers: sender, receiver, message type, correlation id, reply-to, original message id, content type, version, timestamp. |
| M2 | Add Core envelope builder/helper APIs for common patterns: command, request, response, acknowledgment, error, and typed payload response. |
| M3 | Add typed protocol messages for ProjectMemory workflow requests and responses. |
| M4 | Existing string-header behavior remains compatible, but new code should use the shared builders. |
| M5 | Message builders must preserve trace/activity propagation hooks. |
| M6 | Runtime adapters and high-traffic actors should migrate away from repeated standard header literals and use `AgctorMessageHeaders`, `AgctorMessageTypes`, and `AgctorEnvelopeBuilder` where practical. |
| M7 | Add regression tests that catch misspelled or missing standard headers in framework-owned actor protocols. |

### 5.3 Runtime adapter conformance

| ID | Requirement |
| --- | --- |
| R1 | Create adapter-agnostic tests that run against every registered runtime marked as supported. |
| R2 | Define consistent missing actor behavior for send-only and request/response calls. |
| R3 | Define consistent handling for acknowledgments versus final responses. |
| R4 | Define consistent correlation propagation across request, actor response, error response, and timeout. |
| R5 | Runtime dashboard capability data should reflect actual maturity: supported, experimental, placeholder, or unavailable. |
| R6 | Run the conformance suite against `Proto.Actor` for the stable subset, and keep any known drift visible in tests and the tracker until fixed. |
| R7 | Do not promote a runtime from experimental to supported until it passes the required conformance subset for message construction, request/response, correlation, missing actors, and shutdown. |

### 5.4 Agent consistency

| ID | Requirement |
| --- | --- |
| G1 | C# ProjectMemory agents must use the same loaded `AgentDefinitionSpec` instructions as Host and pipeline flows unless they explicitly document an override. |
| G2 | ProjectMemory agents should depend on injected interfaces for LLM, loader, operations, and policy services. |
| G3 | Persona runner and pipeline should share one LLM client abstraction where practical. |
| G4 | Add tests that detect drift between sample agent YAML contracts and C# agent behavior for key agents such as `person-extractor`. |

## 6. Acceptance criteria

1. ProjectMemory actor workflow can run the same happy-path ingest/query scenario as the current pipeline and produce equivalent final text, step summaries, and file updates.
2. Out-of-schema confirmation behavior continues to work through the new workflow or remains explicitly routed through the compatibility path until parity is complete.
3. New actor/protocol code uses shared envelope builders instead of hand-built standard headers.
4. Adapter conformance tests pass for `InMemory`; `Proto.Actor` either passes the supported subset or is clearly marked experimental for failing capabilities.
5. Missing actor behavior is documented and tested.
6. ProjectMemory C# agents no longer contain divergent shortened prompt contracts for the sample YAML agents without an explicit test-covered reason.
7. Existing unit and integration tests continue to pass.

## 7. Security, data, and compatibility

- Existing file writes remain guarded by the same ProjectMemory access checks and scenario scoping rules.
- No new persisted data format is required for Phase 1.
- Trace metadata must remain size-capped and avoid leaking more content than the current pipeline already exposes.
- Compatibility matters because ProjectMemory data and sample projects already exist.

## 8. Open questions

1. Should the actor workflow fully replace `ProjectMemoryPipelineRunner`, or should the runner become a thin facade over actors?
2. Should send-only to a missing actor throw, return a result, or publish a dead-letter event?
3. Should `MessageEnvelope.Metadata` become immutable in a future breaking release?
4. Should `AgentDefinitionSpec` become the only source of prompt instructions for ProjectMemory agents?
5. How much Proto.Actor behavior should be required before the dashboard calls it supported rather than experimental?

## 9. Explicit remaining hardening backlog

The first PRD-020 slices establish the actor workflow and shared protocol helpers, but two risks remain active until fully enforced:

1. **Loose messages.** Existing actors and runtimes still contain repeated string headers and raw payload checks. New code must use shared builders and typed protocol messages; touched legacy paths should be migrated opportunistically.
2. **Runtime drift.** `InMemory` is the supported baseline. `Proto.Actor` remains experimental until its envelope construction, correlation, acknowledgment, error, timeout, and missing-actor behavior pass the conformance suite.

