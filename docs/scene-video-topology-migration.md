# Scene-video topology migration (old admission flows)

Short companion to [scene-video-workflow.md](./scene-video-workflow.md).

## Why

Operators who customized `scene-video-chain` only via the 3/4-node conversation flow need a clear path onto the shared topology document without losing “audio on/off” intent.

## Rules

1. **Product graph** = `Config/scene-video-topology.json` (+ optional `scene-video-topology.user.json`).
2. **Admission flow** = scenario `flow` (Input → Director → Output, optional Audio Engineer). Secondary overlay only.
3. **One-shot seed:** if the topology user overlay is missing, the first topology read copies `audioEnabled` from whether the admission flow contains an Audio Engineer AgentLoop (`role: audio-engineer`), then writes the overlay.
4. **Explicit save wins:** `PUT /api/scenarios/scene-video-chain/topology` always owns `audioEnabled` and `nodeLayouts` afterward.
5. **Runs:** new starts prefer `topology.audioEnabled`; fall back to admission Audio Engineer presence. Old runs keep their checkpointed `AudioEnabled`.

## Files

| File | Role |
| --- | --- |
| `AgctorSDK.Host/Config/scene-video-topology.json` | Shipped fixed roster (do not put operator edits here) |
| `AgctorSDK.Host/Config/scene-video-topology.user.json` | Operator overlay (gitignored) |
| `AgctorSDK.Host/Config/agctor-scenarios.user.json` | Optional admission-flow overrides |

## Code

- `SceneVideoTopologyMigration` — derive / seed audio from admission
- `SceneVideoTopologyCatalog.EnsureAdmissionMigrated` — one-shot persist
- `SceneVideoScenario.StartAsync` — runtime flag preference
