# PRD-012: Actor runtime selection dashboard

## Goals

1. Provide a **dashboard page** where operators can see which **actor runtime backend** is active (InMemory, Proto.Actor, Orleans), with **version**, **initialization state**, and light **statistics** when available.
2. Show a **catalog** of supported runtimes with **human-readable descriptions** and **capability tags** so expectations match each backend (local dev vs remoting vs clustering).
3. Allow **changing the configured runtime** for the **next** Host start by writing **`appsettings.User.json`** (Tier A), with explicit **restart required** messaging (“hot reload” = persist + restart, not silent in-process swap in v1).

## Non-goals (v1)

- **Tier B** in-process runtime swap without process restart (delegating adapter, message drain, singleton lifecycle) — deferred; see **Tier B (future)** below.
- Automated process restart or external supervisor integration.
- New multi-node or cloud deployment infrastructure (see **Future: distributed deployment**).

## User stories

- As an operator, I want to **see the active runtime** and how it differs from **configured next-boot** settings so I know whether a restart will change behavior.
- As an operator, I want **capability tags** (e.g. local dev, remote messaging) so I choose the right backend for my environment.
- As an operator, I want to **save Proto host/port** together with runtime selection when using Proto.Actor.
- As an operator, after saving, I want a clear **restart required** message so I do not assume changes apply immediately.

## Terminology

- **Actor runtime** (this PRD): the **backend implementation** (`IActorRuntimeAdapter`), not the LLM model.

## Dashboard UX

1. **Current runtime** card: canonical id (`InMemory` | `Proto.Actor` | `Orleans`), adapter `Name`, `Version`, `IsInitialized`, optional stats (active actors, uptime, etc.).
2. **Next boot** line: values read from configuration (`Agctor:DefaultRuntime`, `ProtoHost`, `ProtoPort`) which may differ until restart.
3. **Catalog**: one card per available runtime from `IActorRuntimeAdapterFactory.GetAvailableRuntimes()` merged with static **ActorRuntimeCatalog** copy (summary, limitations, deployment notes, capabilities).
4. **Form**: dropdown or radio for `DefaultRuntime`, optional Proto host/port (shown when Proto.Actor selected or always visible with helper text), **Save** → `PUT /api/runtime` → banner: saved; **restart Host** to apply.

## API

### `GET /api/runtime`

Returns:

- `current`: canonical id, adapterName, version, isInitialized, statistics (optional, null if unavailable)
- `configured`: defaultRuntime, protoHost, protoPort (effective config for next boot / file merge)
- `available[]`: id, displayName, summary, limitations, deploymentNotes, capabilities, supportsProtoRemoting (hint for UI)

### `PUT /api/runtime`

Body: `defaultRuntime` (required; accepts aliases `Proto` → `Proto.Actor`), optional `protoHost`, `protoPort`.

Response: `requiresRestart: true`, `persistedCanonicalRuntime`, `message`.

Errors: 400 for unknown runtime; 500 if file write fails.

## Acceptance criteria

1. `/Dashboard/ActorRuntime` loads without JS errors and reflects `GET /api/runtime`.
2. Catalog lists all factory runtimes with non-empty display names and capability arrays.
3. `PUT` persists to `appsettings.User.json` under `Agctor:DefaultRuntime` and optional Proto keys; does not change the active adapter until Host restart.
4. UI states clearly that **restart is required** after a successful save.
5. Unit tests cover catalog ids; component or integration tests cover persistence and `GET` shape.

## Tier B (future): in-process hot swap

**Not in v1.** Requires a delegating `IActorRuntimeAdapter`, defined shutdown/swap/init ordering, handling of in-flight messages, and likely **non-singleton** or **re-initializable** adapter instances so `ShutdownAsync` does not leave a permanently broken singleton.

## Future: distributed deployment

Directional roadmap (documentation only in v1):

| Runtime | Multi-machine / cloud angle |
| --- | --- |
| **InMemory** | Single process only; dev and tests. |
| **Proto.Actor** | Remoting/cluster via `ProtoHost` / `ProtoPort` and cluster config; align with Host `Program.cs` initialization. |
| **Orleans** | Silos, clustering, cloud hosting; adapter maturity tracked in repo. |

Capability flags on the catalog should align with this roadmap without implementing infrastructure.
