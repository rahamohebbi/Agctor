# Agctor

A .NET 8 and .NET 10 actor-model framework for building **agentic** systems: isolated actors,
async message passing, LLM agents, tools, and an HTTP + MCP host.

[![CI](https://github.com/rahamohebbi/Agctor/actions/workflows/ci.yml/badge.svg)](https://github.com/rahamohebbi/Agctor/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

> If you use Agctor, please keep the copyright notices and [NOTICE](NOTICE)
> file, and cite the project with [CITATION.cff](CITATION.cff).

## Why actor model

Agents in Agctor are actors. Each one owns its state, receives messages one at a
time, and talks to other agents only through envelopes. That keeps LLM calls,
tool execution, and human input isolated instead of sharing a global workflow
object.

## Features

- **Actor runtime** with in-memory and Proto.Actor adapters, plus an Orleans integration that currently delegates agent execution to in-memory actors
- **Agents**: LLM (Ollama), human/CLI, and tool actors
- **Tools**: C# and Python code execution, filesystem, code editor
- **Timeouts** as a supervisor actor, with progress and partial results
- **Host**: agent dashboard, scenario workflows, REST API, Swagger, and a TCP MCP listener
- **Project memory and CodeGraph**: project knowledge, retrieval providers, and code analysis
- **CLI** for sending a prompt to a root agent
- **Observability**: logging, metrics, and visualization helpers

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- .NET 8 runtime to run tests or applications targeting `net8.0`
- Optional: [Ollama](https://ollama.com) with a local model (default `mistral`)
  for LLM agents

## Quick start

The Host and its integration tests target .NET 10. SDK libraries, CLI, Orleans silo, demos, and remaining tests target both .NET 8 and .NET 10. `dotnet build` builds all configured targets. The Host starts directly on .NET 10 without framework selection; use `--framework net10.0` (or `net8.0`) when running other applications.

```bash
dotnet restore Agctor.sln
dotnet build Agctor.sln
```

Run the host:

```bash
dotnet run --project AgctorSDK.Host
```

- Dashboard: `http://localhost:5274` (redirects to `/Dashboard`)
- REST / Swagger: `http://localhost:5274/swagger`
- MCP: `127.0.0.1:8080` (loopback by default — see [SECURITY.md](SECURITY.md))

Run a prompt through the CLI:

```bash
dotnet run --project AgctorCLI --framework net10.0 -- "Summarize the actor model in one sentence"
```

## Scene Memory

Open **Scene Memory** in the Host dashboard (`/Dashboard/SceneMemory`). Create a workspace and
describe a scene in **Describe what happens**. Select an installed Ollama chat model, choose
**Preview scene changes**, then **Apply to scene**. Studios are not prerequisites: the model proposes
new characters, objects and places, reuses existing identities, and extracts ordered state changes.
Use the three example buttons to test discovery, a follow-up transfer, and ambiguous references.
The **What will change** preview groups the server-calculated result into entity cards with before/after
values, automatic consequences and source text. **Edit** opens a focused dialog with guided actions;
**Cancel**, **Close** or Escape discards dialog edits. **Done** validates the
correction and updates the cards without saving memory. **Exclude** removes an entity's proposed changes
and its related story moments (and dependent references when excluding a newly discovered entity).
**Story moments** allows editing descriptions, participants and whether an action happened or was only planned.
**Advanced** retains custom fields, precise ordering and JSON; use **Check changes** after editing there.
Unfinished or invalid edits disable Apply. **Restore original** returns to the model's proposal.
The server retains the prompt, selected model, workspace revision and
preview identity, so browser edits cannot bypass stale-preview protection or rewrite source metadata.
Reported ambiguities still require clarifying and previewing the prompt again; row editing cannot silently
dismiss them.

Open **Evaluation suite** from Scene Memory (`/Dashboard/SceneMemoryEvaluation`) to benchmark one to
three installed Ollama chat models without touching saved workspaces. The catalog contains 100 cases
covering possession, ownership, transfer, lifecycle, people, locations, object condition and intent.
Quick runs sample every category; full runs execute sequentially to remain practical on a 16 GB machine.
Each case is reduced in an isolated in-memory workspace and scored against expected entities, final facts,
forbidden facts, clarification questions and events. The dashboard reports pass rate, wall-clock latency,
Host memory change and failure categories, with per-check details and a downloadable JSON result. Evaluation
runs are process-local and disappear when Host restarts; the exported JSON is the durable comparison artifact.

Open **Shot timeline** (`/Dashboard/SceneMemoryTimeline`) to inspect an applied story in narrative order.
Applied prompts are shots; consecutive shots with the same explicit scene name are grouped into scenes. Selecting
a shot shows its prompt, introduced entities, story events, property-level before/after changes and the complete
state as of that shot. Previous/next controls and horizontally scrollable scene groups work on desktop and mobile.
Scene name, shot name and optional planned duration can be edited from the timeline without changing state,
source prompts or sequence order. New prompts accept these fields directly. Older prompts remain compatible and
appear in **Unassigned scene** until named; their historical state is reconstructed from existing change numbers.

For example, `Maya enters the kitchen carrying a red mug. She places it on the counter.` can
discover Maya, Kitchen, Red mug and Counter, record the mug's color and intermediate holder, then
place it on Counter and clear its holder. A follow-up prompt receives current facts and the last four
applied prompts to resolve references. Extraction accuracy depends on the selected model; review it.
Manual entity/property editing and **Set holder / transfer** remain under **Manual setup and corrections**.
Free-text manual property values and event notes are still literal; only the prompt workflow interprets them.

Scene Memory also appears in **RAG providers**, with its own `SceneMemory` kind and an **Open Scene Memory**
action. Knowledge RAG providers retrieve document/graph context; Scene Memory retrieves explicitly recorded
entity state and history. It works alongside the default RAG provider and uses its own store/tool contract.
The catalog API exposes `kind` and `canBeDefaultProvider`; Scene Memory cannot replace the default document
retrieval adapter. Markdown-only retrieval is labeled File memory.

RAG and Scene Memory share `IRetrievalProvider<TRequest, TResult>` for provider identity, health and
typed queries. `IRagProviderAdapter` adds document ingestion; `ISceneMemoryProvider` returns scene state
and history. Host APIs and scene agent tools use the same injected Scene Memory provider. Register it
outside Host with `services.AddAgctorSceneMemory(databasePath)`; pre-registered custom stores and
providers are preserved. This keeps retrieval separate from persistence and domain-specific writes.

Query memory directly, or create a test agent on the same page and ask `Where is CupA?`.
The test agent uses AGCTOR's runtime and registered `SceneMemoryQueryTool`; no AI model is required.
Both paths return the stored properties with their source change numbers. An optional change number
retrieves an earlier state. Offscreen entities retain their last recorded values.

Retrieval is deterministic: questions match an entity's exact name (case insensitive), or callers
can supply its ID. Ambiguous matches ask for an explicit entity. It does not interpret arbitrary
questions, infer facts from event notes, analyze media, generate clips, or integrate with Cynelo yet.
Audio/music references can be stored as text properties; media files are not uploaded or analyzed.

`ISceneMemoryStore` in Core works independently of actors and RAG providers. SQLite stores the
workspaces and append-only change history at `data/scene-memory.db` under the Host output directory.
Set `Agctor:SceneMemory:StorePath` (environment: `Agctor__SceneMemory__StorePath`) to a durable absolute
path for deployments. Test agents are temporary and must be recreated after a Host/runtime restart.
Sequence numbers represent entry order, not a parsed film timeline. Correct a fact by recording a new value.
Prompt commits are atomic, check the original workspace revision, and are safe to retry. Previews
expire after 20 minutes or a Host restart. Applied prompts, model names, optional shot labels, source
excerpts, intermediate state and cleared properties are persisted. Canonical physical placement
(`held_by`, `on`, `in`, `located_at`) is exclusive; transfers/placements clear conflicting prior placement.
Other attributes and relationships are extensible, with one current value per entity/property.
Actor-side `has`, `holds`, `holding`, `carries` and `carrying` relations normalize to object-side
`held_by`, so both object and character queries agree and multiple carried objects do not overwrite
each other. `held_by` includes carrying in a pocket or bag; `carried_at` preserves that detail and is
cleared when possession changes or ends. Ownership alone (`owned_by`) does not imply physical possession.
Ownership discovers the item as an entity: “ActorA owns a red hat” creates Red hat with `color=red`
and `owned_by=ActorA`. Ownership is a typed relation, so a plain-text item name on the owner is rejected
and the interpreter gets one repair attempt. `owns` relations are normalized onto the item, and queries
of the owner return all owned items. The object table shows owner and physical holder separately.
Completed consumption/destruction uses object `status=consumed` / `status=destroyed`, clears physical
placement, and can link `consumed_by` / `destroyed_by` to a character. Reverse actor queries include
those actions without retaining possession. Partial eating or a broken condition does not automatically
remove possession. The interpreter is instructed to distinguish completion, partial actions and negation;
model accuracy still requires review. Invalid model output gets one repair attempt before failing without writes.
Uncovered new entities or occurred/ongoing events without associated state changes also trigger one
completeness review by the model. Event-only results remain allowed; this check cannot prove semantic accuracy.
Scene Memory keeps **events separate from current state**. Each interpreted event includes all participants,
source text and a mode (`occurred`, `ongoing`, `intended`, `hypothetical`, `negated`). Entity queries return
both current `facts` and the matching `events`, including when queried through the provider or test agent.
Events never overwrite each other or become current properties. States and then events are appended per
prompt; sequence is storage order, not exact narrative timing within a shot.

| Prompt | State semantics |
| --- | --- |
| ActorA throws away a pen | Pen `disposition=discarded`; clear previous possession, carrying position and placement. Preserve the object and the event. |
| ActorD finds and picks up that pen | Pen `held_by=ActorD`, `disposition=recovered`. |
| ActorB dies | Character `life_state=dead`; end ongoing activity, retain identity, abilities and unrelated facts. Explicit revival can restore activity. |
| ActorC can sing and dance but got sick | Separate `ability_sing=true`, `ability_dance=true`, `health=sick`. No inferred loss of skill. |
| ActorC is too sick to sing today | `availability_sing=unable`; learned skill remains. Explicit recovery can update health and availability. |
| The garden is destroyed | Location `status=destroyed`; do not invent injuries or death for its occupants. |
| Maya plans to drop the cup but does not | Intended/negated events; no completed transfer. |

Open attributes cover emotions, appearance, weather, lighting, accessibility, power, locks, damage,
repair, sound and other story details; independent dimensions use separate properties. This is an
extensible vocabulary, not a complete physical simulator. Interpretation of meaning still depends on
the selected model and the preview review. Hard state rules enforce canonical lifecycle consequences;
they do not prove that a model understood every sentence. **Explore more situations** on the Scene Memory
page loads example prompts with expected effects. **Story events** and query results show what happened
alongside **Saved properties**. Existing saved workspaces remain readable; past prompts are not reinterpreted.

These are planned story facts, not verified observations of generated video. Take branching, timeline
editing, media analysis and Cynelo Studio linking are not implemented; use separate workspaces for alternatives.

The provider API follows the RAG pattern: `GET /api/scene-memory/health` uses the same health response
shape, and `POST /api/scene-memory/query` accepts `workspaceId`, `query` or `entityId`, and optional
`atSequence`. Query responses share `success`, `providerId` and `message` with RAG, plus a typed `result`
containing scene facts, source changes and the requested sequence.

Existing workspace endpoints remain under `/api/scene-memory/workspaces`: create/list workspaces, then use
`/{id}/entities`, `/{id}/facts`, and `/{id}/query`. Query bodies accept `entityId` or `question`, plus
optional `atSequence`. `/{id}/agents` creates/lists test agents, and `/{id}/agents/{agentId}/query`
exercises the agent-to-tool path. Other agents can invoke the registered `SceneMemoryQueryTool`
with operation `Query` and parameters `workspaceId`, `entityId` or `query` (`question` remains an alias), and optional `atSequence`.
The Host tool catalog also exposes it as `scene-memory-query`. Like the existing Host APIs, these
endpoints assume a trusted local environment; workspace separation is not an authorization boundary.

Prompt APIs: `POST /api/scene-memory/workspaces/{id}/prompts/preview` accepts `prompt`, optional `label`
and optional `model` (otherwise the configured default chat model). It returns `preview` and `proposed` state
without changing the workspace. `POST .../prompts/apply` accepts only `{ "previewId": "..." }`, applying the
server-held preview. Unknown, expired, ambiguous and stale previews return a conflict without partial writes.
Ollama is required for interpretation; applying a preview and querying saved facts require no model call.

## Testing

```bash
dotnet test AgctorSDK.Core.Tests
dotnet test AgctorSDK.Core.IntegrationTests
dotnet test AgctorSDK.Host.IntegrationTests
```

Tests tagged `RequiresOllama` need a running Ollama daemon. CI excludes those core integration tests. Multi-targeted tests run on both runtimes by default; use `--framework net10.0` to test only .NET 10. Host integration tests run on .NET 10 only.

Publish the Host or Orleans silo for .NET 10:

```bash
dotnet publish AgctorSDK.Host -c Release --framework net10.0 -o artifacts/host/net10.0
dotnet publish AgctorSDK.OrleansSilo -c Release --framework net10.0 -o artifacts/orleans/net10.0
```

The Orleans Docker image defaults to .NET 10. Pass `--build-arg DOTNET_VERSION=8.0` to build it for .NET 8.

## Repository layout

```
AgctorSDK.Core/                 # Contracts, messages, timeouts, observability
AgctorSDK.Agents/               # Agent actors and in-memory runtime
AgctorSDK.Tools/                # Tool actors (code, files)
AgctorSDK.Extensions/           # DI: AddAgctor()
AgctorSDK.Host/                 # HTTP + MCP gateway
AgctorCLI/                      # Command-line runner
AgctorSDK.Core.Tests/           # Unit tests
AgctorSDK.Core.IntegrationTests/
AgctorSDK.Host.IntegrationTests/
Demo/                           # Visualization samples
Project/                        # Product requirements (not source)
```

## Using it as a library

```csharp
using AgctorSDK.Core.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgctor(); // in-memory runtime by default
```

Packable projects (`Core`, `Agents`, `Tools`, `Extensions`) are Apache-2.0 and
include LICENSE + NOTICE in the nupkg.

## Security

Code execution and filesystem tools run **in-process with no sandbox**. The host
has no authentication yet. Use Agctor only in environments you trust. Details:
[SECURITY.md](SECURITY.md).

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports and small, tested
PRs are the fastest path to a merge.

## Citation

```bash
# GitHub: use "Cite this repository" from CITATION.cff
```

## License

Copyright 2026 Raha Mohebbi and Agctor contributors.

Licensed under the [Apache License, Version 2.0](LICENSE).
You must retain attribution (copyright headers / NOTICE) when you redistribute.
See [NOTICE](NOTICE).
