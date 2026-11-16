# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/) once versions
are tagged.

## [Unreleased]

### Added

- Scene-video Phase A topology: shared `Config/scene-video-topology.json`,
  `GET /api/scenarios/scene-video-chain/topology`, and graph responses that
  union the definition skeleton with observed routes (Visual critic alias).
- Live agent-run graph on Scenarios and ScenarioRun, rendering the same
  `GET /api/agent-runs/{id}/graph` topology Cynelo’s Agent activity view shows.
- Scenarios overview keeps the selected run id when remounting the live graph,
  and labels AgentLoop “Edit flow” as loop wiring so it is not mistaken for the
  live Director/specialist topology.
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

- Moved IronPython and Roslyn package references from Core to Tools, where the
  language executors live.
- Removed unused compile-excluded leftovers (placeholder tests, `.bak`, duplicate
  options, library demo entry points).

### Security

- Documented the trusted-operator model for code execution and filesystem tools.
