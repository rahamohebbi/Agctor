# RAG provider Docker sidecars (PRD-025)

Sidecars for the AGCTOR **RAG providers** dashboard (`/Dashboard/RagProviders`).

| Service | Image | Default port | Agctor provider id |
| --- | --- | --- | --- |
| `lightrag` | `ghcr.io/hkuds/lightrag:latest` | 9621 | `LightRAG` |
| `graphiti` (+ `graphiti-db`) | `zepai/graphiti:latest` + `neo4j:5.26.2` | 8001 | `Graphiti` |
| `cognee-mcp` | `cognee/cognee-mcp:main` | 8000 | `Cognee` |

**First start:** Cognee MCP image is ~6 GB — `pull` or `up -d` can take **2–5 minutes** the first time. Graphiti also pulls Neo4j. Use **Pull** in the dashboard, wait for completion, then **Start**.

## Ollama prerequisites (required for local LLM + embeddings)

LightRAG and Cognee default to **Ollama on the host** (`host.docker.internal:11434`). Graphiti defaults to **OpenAI**; you can point it at Ollama via `OPENAI_BASE_URL` in `graphiti.env`.

```bash
chmod +x docker/rag-providers/setup-ollama-models.sh
docker/rag-providers/setup-ollama-models.sh
```

Or manually:

```bash
ollama pull gemma3:4b        # LLM (LightRAG extraction + Cognee + optional Graphiti)
ollama pull bge-m3:latest    # embeddings (LightRAG + Cognee + optional Graphiti)
```

Verify:

```bash
ollama list | grep -E 'gemma3:4b|bge-m3'
curl -fsS http://127.0.0.1:11434/api/tags
```

If you change models in `*.env`, update `HUGGINGFACE_TOKENIZER` in `cognee.env` to match the embedding model (see [Cognee embedding docs](https://docs.cognee.ai/setup-configuration/embedding-providers)).

## Quick start

```bash
cp docker/rag-providers/lightrag.env.example docker/rag-providers/lightrag.env
cp docker/rag-providers/graphiti.env.example docker/rag-providers/graphiti.env
cp docker/rag-providers/cognee.env.example docker/rag-providers/cognee.env
# Edit *.env with your LLM / embedding settings

docker compose -f docker/rag-providers/docker-compose.yml pull lightrag
docker compose -f docker/rag-providers/docker-compose.yml up -d lightrag
curl http://127.0.0.1:9621/health
```

Graphiti REST (starts Neo4j dependency automatically):

```bash
# Set OPENAI_API_KEY in graphiti.env (or Ollama via OPENAI_BASE_URL)
docker compose -f docker/rag-providers/docker-compose.yml up -d graphiti
curl http://127.0.0.1:8001/healthcheck
```

Cognee MCP (HTTP transport on `/mcp`):

```bash
docker compose -f docker/rag-providers/docker-compose.yml up -d cognee-mcp
```

Configure Agctor Host (`appsettings.User.json`):

```json
{
  "Agctor": {
    "Rag": {
      "DefaultProvider": "LightRAG",
      "LightRAG": { "BaseUrl": "http://127.0.0.1:9621" },
      "Graphiti": { "BaseUrl": "http://127.0.0.1:8001", "DefaultGroupId": "agctor" },
      "Cognee": { "BaseUrl": "http://127.0.0.1:8000", "McpPath": "/mcp" }
    }
  }
}
```
