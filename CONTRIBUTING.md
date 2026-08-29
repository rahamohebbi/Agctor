# Contributing to Agctor

Thanks for helping improve Agctor. This project is an actor-model framework for
agentic systems. Contributions that strengthen that model — isolated actors,
message passing, supervision, and location transparency — are especially welcome.

By submitting a contribution, you agree that your work is licensed under the
[Apache License 2.0](LICENSE) (inbound = outbound). Please keep copyright notices
and the [NOTICE](NOTICE) file intact.

Please also follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Ways to contribute

- Bug reports and reproductions
- Tests (unit tests first, then integration tests)
- Documentation and examples
- Features that stay modular and avoid duplicating existing actors or tools

If you use Agctor in a paper, product, or talk, citing it via [CITATION.cff](CITATION.cff)
is the best way to give credit.

## Project layout

Put new code in an existing project. Do not add source under `Project/` — that
folder is for product requirements only.

| Location | What belongs there |
|---|---|
| `AgctorSDK.Core` | Actor contracts, messages, timeouts, observability |
| `AgctorSDK.Agents` | Agent actors (LLM, human, factory, in-memory runtime) |
| `AgctorSDK.Tools` | Tool actors (code execution, filesystem, editors) |
| `AgctorSDK.Extensions` | DI wiring (`AddAgctor`, runtime adapters) |
| `AgctorSDK.Host` | HTTP + MCP gateway |
| `AgctorCLI` | Command-line runner |
| `AgctorSDK.Core.Tests` | Unit tests |
| `AgctorSDK.Core.IntegrationTests` | Core integration tests |
| `AgctorSDK.Host.IntegrationTests` | Host/API/MCP integration tests |
| `Demo/` | Sample applications |

## Development setup

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Clone the repository and restore:

```bash
dotnet restore Agctor.sln
dotnet build Agctor.sln
```

3. Run tests after any non-trivial change:

```bash
dotnet test AgctorSDK.Core.Tests
dotnet test AgctorSDK.Core.IntegrationTests
dotnet test AgctorSDK.Host.IntegrationTests
```

LLM end-to-end tests need a local [Ollama](https://ollama.com) instance with the
`mistral` model. They are tagged `RequiresOllama` and are skipped in CI when
Ollama is not present.

Optional: run the host and browse Swagger at `http://localhost:5000/swagger`.

```bash
dotnet run --project AgctorSDK.Host
```

## Coding guidelines

- Prefer short, meaningful names.
- Add a short comment where the *why* is not obvious from the code.
- Keep types small and reusable. Avoid duplication.
- All potentially blocking work should be `async` and honor `CancellationToken`.
- New public APIs should have XML docs.
- Do not commit secrets, user files, `bin/`, `obj/`, or generated HTML dumps.
- Do not expose the MCP listener or code-execution tools on untrusted networks.

## Pull requests

1. Create a focused branch from `main`.
2. Include tests for behavior changes.
3. Run the build and the test suites above.
4. Fill out the pull request template.

Maintainers may ask for smaller PRs if a change mixes unrelated work.
