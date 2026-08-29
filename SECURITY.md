# Security Policy

## Supported versions

Agctor is pre-1.0. Security fixes are applied on `main`. Please upgrade to the
latest commit (or release, once tagged) rather than carrying private patches.

## Reporting a vulnerability

**Do not open a public GitHub issue for security bugs.**

Please report vulnerabilities through
[GitHub Security Advisories](https://github.com/rahamohebbi/Agctor/security/advisories/new)
or email [rahamohebbi@gmail.com](mailto:rahamohebbi@gmail.com).

Include:

- A description of the issue and impact
- Steps to reproduce, or a proof of concept
- Affected commit / version if known

You should receive an acknowledgement within a few days. We will coordinate a
fix and public disclosure with you.

## Trust model (read this before deploying)

Agctor is designed for **trusted operators** building agent systems. Several
components can take destructive actions if an untrusted party can send them
messages:

| Component | Risk |
|---|---|
| `CodeExecutorTool` | Compiles and runs C# (Roslyn) and Python (IronPython) **in-process**. There is no OS sandbox. |
| `FileSystemTool` | Reads and writes any path the process can access. Paths are not rooted or allow-listed. |
| MCP listener | Accepts TCP messages and routes them to agents. Default bind is loopback (`127.0.0.1`). Binding `0.0.0.0` exposes it to the network. |
| Host HTTP API | No authentication in this version. Do not publish it to the public internet. |

Recommended defaults:

- Run Host/CLI only on a machine you control.
- Keep `Mcp:Host` at `127.0.0.1` unless you add your own auth and firewall.
- Do not wire code-execution or filesystem tools to agents that accept untrusted prompts without a sandbox you own.
- Keep Swagger and CORS (`AllowAll`) limited to development.

## Secrets

Never commit API keys, connection strings, or `appsettings.*.local.json`. Use
environment variables or [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
for local overrides.
