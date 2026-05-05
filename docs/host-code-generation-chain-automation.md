# AGCTOR Host Automation Guide (Code Generation Chain)

This guide defines a repeatable API workflow for automated testing of `AgctorSDK.Host` and the `code-generation-chain` scenario.

It covers:

- starting `AgctorSDK.Host/AgctorSDK.Host.csproj`
- setting up `AgctorSDK.Host/Services/Scenarios/CodeGenerationChainScenario.cs`
- creating a simple C# HelloWorld app through API calls
- modifying source code through API calls
- verifying each critical step with machine-checkable assertions

## Why This Document Exists

Use this document as a contract test for host behavior.  
It validates scenario orchestration and tool API availability so regressions are detected early.

## Prerequisites

- .NET 8 SDK installed
- `curl` available
- `jq` available (recommended for assertions)
- Repository root as current working directory

## Actor runtime (InMemory vs Proto.Actor vs Orleans)

The Host selects the **actor runtime** from configuration key **`Agctor:DefaultRuntime`** before HTTP comes up. Typical values: **`InMemory`** (default, single process), **`Proto.Actor`** (optional remoting via **`Agctor:ProtoHost`** / **`Agctor:ProtoPort`**), **`Orleans`**.

Override per run (same `--` / env-var patterns as LLM settings):

```bash
dotnet run --project AgctorSDK.Host/AgctorSDK.Host.csproj -- --Agctor:DefaultRuntime=InMemory
```

```bash
export Agctor__DefaultRuntime=InMemory
dotnet run --project AgctorSDK.Host/AgctorSDK.Host.csproj
```

Persist for the next restart with **`appsettings.User.json`** or **`PUT /api/runtime`**, then restart the Host. Full examples and API shapes: **`AgctorSDK.Host/README.md`** → *Configuration* → *Actor runtime*.

## Ollama Setup and Model Targeting

`CodeGenerationChainScenario` creates an `LLMAgent`, and `LLMAgent` talks to Ollama on:

- base URL: `http://localhost:11434`
- generate endpoint: `POST /api/generate`
- fallback default model in code: `mistral` (overridden by `Agctor:LLM:DefaultModel`)

### A) Install and start Ollama locally

Install Ollama (macOS):

```bash
brew install ollama
```

Start Ollama service:

```bash
ollama serve
```

Verify connectivity:

```bash
curl -sS "http://localhost:11434/api/tags" | jq
```

### B) Pull the model expected by default (`gemma4:31b`)

If you are using appsettings configuration, pull the model configured in:

- `Agctor:LLM:DefaultModel`

If you did not override config, the code fallback is still `mistral` until you set `Agctor:LLM:DefaultModel` (see `AgctorSDK.Host/README.md` and `appsettings.json`).

```bash
ollama pull gemma4:31b
```

Quick model check:

```bash
curl -sS "http://localhost:11434/api/generate" \
  -H "Content-Type: application/json" \
  -d '{"model":"gemma4:31b","prompt":"Say hello in one line.","stream":false}' | jq
```

### C) Target a specific local model (via appsettings)

This repository now supports model configuration through `appsettings.json`.

Update `AgctorSDK.Host/appsettings.json`:

```json
{
  "Agctor": {
    "LLM": {
      "OllamaApiUrl": "http://localhost:11434",
      "DefaultModel": "gemma4:31b"
    }
  }
}
```

Then restart Host:

```bash
ASPNETCORE_URLS="http://127.0.0.1:5055" \
dotnet run --project AgctorSDK.Host/AgctorSDK.Host.csproj
```

Host startup prints the configured values, so you can confirm which model is active.

After changing the model, verify with:

```bash
ollama pull gemma4:31b
curl -sS "http://localhost:11434/api/generate" \
  -H "Content-Type: application/json" \
  -d '{"model":"gemma4:31b","prompt":"Respond with OK.","stream":false}' | jq -e '.response != null'
```

If you want a different model (for example the code fallback `mistral`), set:

```json
"DefaultModel": "mistral"
```

### D) Troubleshooting

- If Host runs but LLM responses fail, check Ollama first:
  - `curl -sS http://localhost:11434/api/tags | jq`
- If model is missing, pull it:
  - `ollama pull gemma4:31b` (or whatever matches your `DefaultModel`)
- If port is occupied, stop conflicting process or restart Ollama on default port `11434`.

## 1) Start `AgctorSDK.Host`

Use a fixed URL to make automation stable across machines and CI agents.

```bash
ASPNETCORE_URLS="http://127.0.0.1:5055" \
dotnet run --project AgctorSDK.Host/AgctorSDK.Host.csproj
```

In a second terminal, verify host readiness:

```bash
curl -sS "http://127.0.0.1:5055/swagger/index.html" > /dev/null && echo "host-ready"
```

Expected output:

```text
host-ready
```

Configured generated-code root (from `AgctorSDK.Host/appsettings.json`):

```json
"Agctor": {
  "GeneratedCodeRoot": "/tmp/agctor-generated-code"
}
```

## 2) Setup `CodeGenerationChainScenario`

### 2.1 Discover available scenarios

```bash
curl -sS "http://127.0.0.1:5055/api/test/scenarios" | jq
```

Expected key:

- `code-generation-chain`

### 2.2 Get scenario info

```bash
curl -sS "http://127.0.0.1:5055/api/test/scenarios/code-generation-chain" | jq
```

Expected fields:

- `.name == "code-generation-chain"`
- `.description` is present

### 2.3 Setup the scenario

```bash
curl -sS -X POST "http://127.0.0.1:5055/api/test/setup-scenario" \
  -H "Content-Type: application/json" \
  -d '{
    "scenarioName": "code-generation-chain",
    "parameters": {}
  }' | jq
```

Expected response characteristics:

- `.success == true`
- `.scenarioName == "code-generation-chain"`
- On a fresh start, `.createdAgentIds` typically includes:
  - `llm-agent`
  - `code-executor-tool`
- On repeated runs, `.createdAgentIds` may be empty because IDs already exist.
- `root-agent` may be absent in the current Proto runtime path; do not hard-fail on that ID.

## 3) Create a Simple C# HelloWorld Application (via `curl`)

Define a reusable temp project directory:

```bash
export AGCTOR_ROOT="/tmp/agctor-generated-code"
export AGCTOR_HELLO_DIR="agctor-hello-world"
```

### 3.1 Create `HelloWorld.csproj` with `file-system` tool

```bash
curl -sS -X POST "http://127.0.0.1:5055/api/tools/file-system/invoke" \
  -H "Content-Type: application/json" \
  -d "{
    \"parameters\": {
      \"operation\": \"write\",
      \"path\": \"${AGCTOR_HELLO_DIR}/HelloWorld.csproj\",
      \"content\": \"<Project Sdk=\\\"Microsoft.NET.Sdk\\\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>\"
    },
    \"timeoutSeconds\": 20
  }" | jq
```

### 3.2 Create `Program.cs` with `file-system` tool

```bash
curl -sS -X POST "http://127.0.0.1:5055/api/tools/file-system/invoke" \
  -H "Content-Type: application/json" \
  -d "{
    \"parameters\": {
      \"operation\": \"write\",
      \"path\": \"${AGCTOR_HELLO_DIR}/Program.cs\",
      \"content\": \"Console.WriteLine(\\\"Hello, World!\\\");\"
    },
    \"timeoutSeconds\": 20
  }" | jq
```

### 3.3 Simulate execution of HelloWorld logic with `code-executor`

```bash
curl -sS -X POST "http://127.0.0.1:5055/api/tools/code-executor/invoke" \
  -H "Content-Type: application/json" \
  -d '{
    "parameters": {
      "language": "csharp",
      "code": "using System; Console.WriteLine(\"Hello, World!\");",
      "timeout": 10
    },
    "timeoutSeconds": 30
  }' | jq
```

## 4) Modify the Source Code (via `curl`)

Use `code-editor` to apply a source change intent:

```bash
curl -sS -X POST "http://127.0.0.1:5055/api/tools/code-editor/invoke" \
  -H "Content-Type: application/json" \
  -d "{
    \"parameters\": {
      \"operation\": \"edit\",
      \"file\": \"${AGCTOR_HELLO_DIR}/Program.cs\",
      \"changes\": {
        \"find\": \"Hello, World!\",
        \"replace\": \"Hello from AGCTOR!\"
      }
    },
    \"timeoutSeconds\": 20
  }" | jq
```

Optional follow-up check through `file-system`:

```bash
curl -sS -X POST "http://127.0.0.1:5055/api/tools/file-system/invoke" \
  -H "Content-Type: application/json" \
  -d "{
    \"parameters\": {
      \"operation\": \"read\",
      \"path\": \"${AGCTOR_HELLO_DIR}/Program.cs\"
    }
  }" | jq
```

## 5) Verification Rules for Steps 2, 3, and 4

These checks are intended for CI automation.

### Verify Step 2 (scenario setup)

```bash
resp="$(curl -sS -X POST "http://127.0.0.1:5055/api/test/setup-scenario" \
  -H "Content-Type: application/json" \
  -d '{"scenarioName":"code-generation-chain","parameters":{}}')"

echo "$resp" | jq -e '
  .success == true and
  .scenarioName == "code-generation-chain"
'

# Accept either:
# 1) newly created agents in this call, or
# 2) agents already present from earlier setup calls.
echo "$resp" | jq -e '
  (.createdAgentIds | index("llm-agent")) != null and
  (.createdAgentIds | index("code-executor-tool")) != null
' > /dev/null || \
curl -sS "http://127.0.0.1:5055/api/agents" | jq -e '
  (map(.id) | index("llm-agent")) != null and
  (map(.id) | index("code-executor-tool")) != null
'
```

### Verify Step 3 (HelloWorld creation/execution path)

```bash
curl -sS -X POST "http://127.0.0.1:5055/api/tools/code-executor/invoke" \
  -H "Content-Type: application/json" \
  -d '{"parameters":{"language":"csharp","code":"Console.WriteLine(\"Hello, World!\");"}}' \
| jq -e '
  .status == 0 and
  .result != null and
  .result.language == "csharp"
'
```

### Verify Step 4 (source modification path)

```bash
curl -sS -X POST "http://127.0.0.1:5055/api/tools/code-editor/invoke" \
  -H "Content-Type: application/json" \
  -d '{"parameters":{"operation":"edit","file":"agctor-hello-world/Program.cs","changes":{"replace":"Hello from AGCTOR!"}}}' \
| jq -e '
  .status == 0 and
  .result != null and
  .result.operation == "edit"
'
```

Direct shell verification (actual file content):

```bash
test -f "/tmp/agctor-generated-code/agctor-hello-world/Program.cs" && \
grep -q 'Hello from AGCTOR!' "/tmp/agctor-generated-code/agctor-hello-world/Program.cs"
```

## 6) Full Automation Script (Example)

```bash
#!/usr/bin/env bash
set -euo pipefail

BASE_URL="http://127.0.0.1:5055"
AGCTOR_ROOT="/tmp/agctor-generated-code"
AGCTOR_HELLO_DIR="agctor-hello-world"

echo "Checking host..."
curl -sS "$BASE_URL/swagger/index.html" > /dev/null

echo "Setting up scenario..."
curl -sS -X POST "$BASE_URL/api/test/setup-scenario" \
  -H "Content-Type: application/json" \
  -d '{"scenarioName":"code-generation-chain","parameters":{}}' \
| jq -e '.success == true'

echo "Creating HelloWorld project..."
curl -sS -X POST "$BASE_URL/api/tools/file-system/invoke" \
  -H "Content-Type: application/json" \
  -d "{
    \"parameters\":{
      \"operation\":\"write\",
      \"path\":\"${AGCTOR_HELLO_DIR}/Program.cs\",
      \"content\":\"Console.WriteLine(\\\"Hello, World!\\\");\"
    }
  }" \
| jq -e '.status == 0'

echo "Verifying created source on disk..."
test -f "${AGCTOR_ROOT}/${AGCTOR_HELLO_DIR}/Program.cs"
grep -q 'Hello, World!' "${AGCTOR_ROOT}/${AGCTOR_HELLO_DIR}/Program.cs"

echo "Modifying source..."
curl -sS -X POST "$BASE_URL/api/tools/code-editor/invoke" \
  -H "Content-Type: application/json" \
  -d "{
    \"parameters\":{
      \"operation\":\"edit\",
      \"file\":\"${AGCTOR_HELLO_DIR}/Program.cs\",
      \"changes\":{\"replace\":\"Hello from AGCTOR!\"}
    }
  }" \
| jq -e '.status == 0'

echo "Verifying edited source on disk..."
grep -q 'Hello from AGCTOR!' "${AGCTOR_ROOT}/${AGCTOR_HELLO_DIR}/Program.cs"

echo "All checks passed."
```

## Important Note About Current Tool Behavior

Current behavior in `AgctorSDK.Host/Services/ToolInvoker.cs`:

- `file-system` and `code-editor` perform real file operations under `Agctor:GeneratedCodeRoot`.
- `code-executor` is still simulated for stable API-contract testing.
