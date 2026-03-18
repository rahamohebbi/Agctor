# PRD-005: Flow-Centric Web UI for AGCTOR

## Purpose

Design a flow-centric web interface that enables humans to accomplish goals using AGCTOR by composing Agents, Tools, and Core Services into reusable flows. The UI proposes a sensible default flow per goal type, keeps Admins in the loop via approval gates, and raises “capability proposals” when the system detects missing Agents/Actors/Tools. Inspired by visual DAG-based builders such as Langflow’s flows model [reference](https://docs.langflow.org/concepts-flows).

## Scope

- In-scope: Web UI, flow templates, admin approvals, run console, review/diff surfaces, agent/tool registry views, observability dashboards, capability proposal workflow, integration with `AgctorSDK.Host` APIs.
- Out-of-scope (MVP): Live collaborative editing, multi-repo orchestration, deployment automation, custom marketplace distribution.

## Personas

- Maker (Developer/PM): defines goals, runs flows, reviews outputs.
- Admin: approves high-impact actions, manages policies/agents, triages proposals.
- Reviewer: reviews diffs/tests, gives feedback.
- Ops: monitors health, runtime adapters, metrics.

## Goals

- Provide a Default Flow per goal type (Coding, Understanding, PR Review) that a user can run with minimal configuration.
- Allow customization via a visual canvas (nodes/edges) similar to flow builders [reference](https://docs.langflow.org/concepts-flows).
- Keep Admin continuously in the loop with explicit approval gates and an Admin Inbox.
- When a capability is missing, generate a structured proposal to create/extend an Agent/Actor/Tool and route it to Admin.
- Ensure traceability: artifacts, logs, snapshots, diffs, PRs, and approvals are linked to runs.

## Non-Goals

- Replacing code editors or external CI/CD systems.
- Enabling arbitrary plugin execution without policy controls.

## Information Architecture

- Dashboard: recent runs, quick actions, pending approvals, health.
- Task Composer: goal input (chat + form), flow selection, preflight checks.
- Flow Canvas: node/edge editor with configurable nodes, gates, and policies.
- Run Console: live run view (timeline, tool calls, artifacts, retries).
- Agent/Tool Registry: discover, configure, enable/disable.
- Review Center: snapshots/diffs, test results, approvals.
- CodeGraph Explorer: embed `Project/mermaid/index.html` rendering `Project/mermaid/diagram.mermaid`.
- Admin Inbox: approvals, capability proposals, policies, audit.
- Observability: metrics, traces, runtime adapters status.
- Settings: connections (Git, MCP, LLM), policies, secrets.

## Default Flow Templates (MVP)

### Coding (feature/refactor/bugfix)

- Nodes: IntentDetection → ContextGathering → Planning → Edits → Tests → Snapshots/Diff → Review → PR.
- Agents/Tools: `IntentDetectionAgent`, `IndexerAgent`, `RefactorAgent` or `LLMAgent`, `CodeEditorTool`, `CodeExecutorTool`, `SnapshotService`/`SnapshotDiffService`, `CodeReviewerAgent`, `PullRequestAgent`.
- Gates: approvals before Apply Edits, Create PR, Merge.

### Code Understanding

- Nodes: IntentDetection → ContextGathering → Comprehension → Snippets.
- Agents/Services: `IndexerAgent`, `ComprehensionAgent`, `Snippets` providers.
- No write/PR steps by default.

### PR Review

- Nodes: Snapshot A/B → Diff → Automated Review → Suggestions → Optional Edit Plan.
- Agents/Services: `SnapshotService`, `SnapshotDiffService`, `CodeReviewerAgent`, optional `RefactorAgent` for suggestions.

## Flow Execution Model

- Node = Agent/Tool/Service/Gate. Edge = typed message contract (e.g., `ProcessPromptMessage`, `AssignSubtaskMessage`, `ToolRequest`, `ToolResult`).
- Correlation per Run and per Node Step to link logs/artifacts.
- Policies per node: timeouts, retries/backoff, fallback, approval requirement.
- Artifacts per step: logs, diffs, snapshots, test results, PR metadata.

## Orchestration Architecture

- Current: scenario system and `TaskFlowHostedService` in `AgctorSDK.Host/Services/TaskFlowHostedService.cs` execute periodic task runs.
- Target: extract orchestration into a dedicated assembly (proposed: `AgctorSDK.Flows`) that provides:
  - `FlowDefinition` (nodes, edges, gates, policies)
  - `FlowRunner` (DAG executor with parallelism/backpressure)
  - `FlowStore` (definitions and run histories)
  - `FlowEvents` (for UI streaming via WebSocket/SSE)
- `AgctorSDK.Host` remains a thin transport: HTTP/MCP (`AgentsController`, `GoalsController`, `MessageDispatcher`) and event streaming.
- `TaskFlowHostedService` behavior can be wrapped or replaced by `FlowRunner` to execute DAG nodes instead of only “ready tasks”.

## Admin-In-The-Loop

- Approval nodes can be placed before risky actions (file writes, PR creation, merges).
- Policies: scope-based (paths/projects), agent/tool type, time windows, risk scores.
- Admin Inbox provides triage, comments, approve/deny with audit trail.

## Capability Proposal Workflow

- Triggered when planner cannot map a flow step to an installed capability.
- Proposal contents: problem statement, desired inputs/outputs, draft interface (Agent/Actor/Tool), reuse/extension options, effort/risks, relevant code links, and suggested scaffolding task.
- Actions: approve, request changes, spawn Scaffold task (delegates to editing/testing agents/tools to prepare a PR).

## UI Surfaces (Details)

- Task Composer: natural language + structured form, intent preview, recommended Default Flow, configurable parameters, preflight checks.
- Flow Canvas: visual DAG editor; node inspector (config, inputs/outputs, policy, risk level); approval nodes.
- Run Console: timeline of node executions, tool calls, artifacts; retry/rollback; streaming updates.
- Registry: list Agents/Tools with capabilities, configs, versions; enable/disable; propose-new-capability.
- Review Center: show diffs/snapshots/tests; add review comments; approve/apply suggestions (post-MVP for apply).
- CodeGraph Explorer: embed `Project/mermaid/index.html` to render `Project/mermaid/diagram.mermaid` as the single source of truth.
- Observability: metrics (latency, success rates), queue depth, runtime adapter health; drill-down to spans.

## Data Model (Conceptual)

- FlowDefinition: id, name, nodes[], edges[], policies, version, owner.
- Node: id, kind (Agent/Tool/Service/Gate), implementation, inputs/outputs, config, approvalRequired.
- Edge: from, to, condition (success/failure/always), messageType.
- Run: id, flowId, inputs, status, started/ended, steps[], artifacts[], approvals[].
- Artifact: kind (snapshot/diff/test/log/pr), uri, metadata.
- Proposal: id, sourceRunId, spec, status, discussion.
- Approval: id, subject (node/run), policy, approver, decision, timestamp.

## Integrations

- `AgctorSDK.Host` controllers (`AgentsController`, `GoalsController`) to start runs and exchange messages.
- `MessageDispatcher` routes to agents via `IActorRuntimeAdapter` (InMemory/Orleans/Proto).
- `SnapshotService` and `SnapshotDiffService` for code review.
- Embeddings via `IndexerAgent` and vector store.
- LLM via `ILLMClient` (e.g., Ollama).

## Observability & Audit

- Emit structured events per node step; correlate with run id and step id.
- Persist logs, artifacts, and approvals with immutable audit records.
- Metrics: task success rate, step latency, error classes, approval turnaround time.

## Security & Privacy

- Role-based access (Maker/Admin/Reviewer/Ops).
- Approval gates for risky actions; policy-based automation.
- Sandboxed tool execution where possible; secrets isolated in Settings and never logged.

## Accessibility & UX

- Progressive disclosure; defaults first, advanced settings behind inspectors.
- Clear state and actions; reversible steps (snapshots) and visible audit trails.

## MVP Scope

- Default Flow templates (Coding, Understanding, PR Review) loaded into Flow Canvas.
- Task Composer + Run Console with live updates and artifact rendering.
- Admin Inbox (approvals + capability proposals).
- Registry (read-only capability display and enable/disable).
- CodeGraph Explorer embedding of the Mermaid diagram.
- Basic Observability dashboard.

## Milestones

1) Foundations: FlowDefinition/Runner APIs (SDK), Host adapters, event streaming.
2) UI Core: Task Composer, Run Console, Default Flows.
3) Admin: Inbox, approvals, policies, audit trail.
4) Review: Snapshots/diffs/test result views.
5) Registry + Explorer: agents/tools listing; embedded mermaid diagram.
6) Capability Proposals: detection → spec → approval → scaffold task.

## Success Metrics

- Time-to-first-successful-run from a default flow (<10 minutes).
- Reduction in manual approvals via safe policies (with zero incidents).
- Mean time to merge PRs produced by flows.
- Coverage of missing-capability proposals leading to accepted scaffolds.

## Risks & Mitigations

- Complexity of DAG editing: ship opinionated defaults; keep canvas optional.
- Approval fatigue: policy grouping, bulk approvals with constraints and strong audit.
- LLM variability: deterministic planning config, retries/backoff, fallback analyzers.
- Drift between UI model and architecture: embed `Project/mermaid/diagram.mermaid` as source of truth.

## Open Questions

- Where to persist FlowDefinitions (file vs DB) and versioning strategy?
- How fine-grained should approval nodes be (per step vs grouped)?
- Which runtime adapter(s) are supported in MVP (InMemory only vs Orleans/Proto)?
- Should proposals auto-generate scaffolding branches by default or wait for explicit Admin approval?

## References

- Langflow – Build flows [reference](https://docs.langflow.org/concepts-flows)
- AGCTOR Host services and controllers: `AgctorSDK.Host/`, `AgctorSDK.Host/Services/TaskFlowHostedService.cs`
- Architecture diagram: `Project/mermaid/diagram.mermaid` rendered by `Project/mermaid/index.html`
