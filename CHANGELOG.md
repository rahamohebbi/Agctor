# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/) once versions
are tagged.

## [Unreleased]

### Added

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
