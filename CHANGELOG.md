# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/) once versions
are tagged.

## [Unreleased]

### Added

- Scene-video Phase E docs/hardening: `docs/scene-video-workflow.md` topology +
  edit/live guidance, `docs/scene-video-topology-migration.md` for old 3/4-node
  admission flows, Host endpoints/architecture diagrams for topology and agent-runs,
  and one-shot `audioEnabled` seed from admission when no topology user overlay exists.
- Scene-video Phase D topology editor: `PUT /api/scenarios/{id}/topology` saves
  audio enable + shared `ui.nodeLayouts` (fixed roster/ownership locked).
- Scene-video Phase C actors: run-scoped Planner, Project, Memory, Render,
  Visual critic, and Editor mailboxes with exclusive tools; Director only
  dispatches (no Role() hat-switching); mailbox isolation enforced in Trace.
- Scene-video Phase B actors: run-scoped Director and Audio Engineer mailboxes;
  Audio Engineer owns audio/speech/narration phases and audio tools; Director
  routes / asks Human / applies audio decisions only (tool matrix enforced).
- Scene-video Phase A topology: shared `Config/scene-video-topology.json`,
  `GET /api/scenarios/scene-video-chain/topology`, and graph responses that
  union the definition skeleton with observed routes (Visual critic alias).
- Live agent-run graph on Scenarios and ScenarioRun, rendering the same
  `GET /api/agent-runs/{id}/graph` topology Cynelo’s Agent activity view shows.
- Scenarios overview keeps the selected run id when remounting the live graph
  (empty-picker fix).
- High-level architecture page (`docs/architecture-diagram.md`) with a generated JPEG.
- MCP connection page (`docs/mcp-diagram.md`) with a generated JPEG.
- Apache License 2.0, NOTICE, and CITATION.cff so redistributors and users can
  keep required attribution.
- Contributor guide, code of conduct, security policy, and GitHub issue/PR templates.
- Shared `Directory.Build.props` package metadata (Apache-2.0, repository URL, symbols).
- GitHub Actions CI that builds the solution and runs unit plus integration tests.
- Safer MCP default bind address (`127.0.0.1`) so the listener is not exposed on
  all interfaces by accident.
- Direct `System.Text.Json` 8.0.5 reference to pick up high-severity JSON fixes.

### Changed

- Single flow graph: Scenarios live view uses the conversation-flow document as the
  definition skeleton (not the topology editor). Topology editor UI removed from
  Scenarios; `GET /api/agent-runs/{id}/graph` returns `flow` + `layouts` projected
  from that canvas so Agctor and Cynelo share one picture.
- Moved IronPython and Roslyn package references from Core to Tools, where the
  language executors live.
- Removed unused compile-excluded leftovers (placeholder tests, `.bak`, duplicate
  options, library demo entry points).

### Security

- Documented the trusted-operator model for code execution and filesystem tools.
