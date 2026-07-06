#!/usr/bin/env bash
# Pull Ollama models referenced by lightrag.env.example and cognee.env.example.
# Run once before starting RAG sidecars (Ollama must be running on the host).
set -euo pipefail

if ! command -v ollama >/dev/null 2>&1; then
  echo "Error: ollama CLI not found. Install from https://ollama.com/download" >&2
  exit 1
fi

if ! curl -fsS http://127.0.0.1:11434/api/tags >/dev/null 2>&1; then
  echo "Error: Ollama is not reachable at http://127.0.0.1:11434. Start Ollama first." >&2
  exit 1
fi

LLM_MODEL="${LIGHTRAG_LLM_MODEL:-gemma3:4b}"
EMBED_MODEL="${LIGHTRAG_EMBED_MODEL:-bge-m3:latest}"

echo "Pulling LLM model: ${LLM_MODEL}"
ollama pull "${LLM_MODEL}"

echo "Pulling embedding model: ${EMBED_MODEL}"
ollama pull "${EMBED_MODEL}"

echo "Done. Installed models:"
ollama list | grep -E "${LLM_MODEL}|${EMBED_MODEL}" || ollama list
