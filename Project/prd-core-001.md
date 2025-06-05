# PRD: LLMAgent for Local LLM (Ollama)

## Feature Name
LLMAgent (Local LLM Interface)

---

## Objective
Create an agent that allows the system to communicate with a locally running LLM (e.g., Mistral via Ollama) to generate responses from prompt inputs.

---

## User Story
> As an agent, when I need to generate or explain content, I want to delegate the task to an LLMAgent that talks to a local LLM (Ollama), so I can receive high-quality text-based outputs.

---

## Requirements
1. **LLMAgent Implementation**
   - Implements `IActor` interface.
   - Accepts a text-based prompt through the `HandleAsync()` method.
   - Sends the prompt to `http://localhost:11434/api/generate` (Ollama API).
   - Uses default model (e.g., `"mistral"`).

2. **HTTP Communication**
   - Use `HttpClient` to make POST request to Ollama.
   - Parse and return only the LLM's textual response as `MessageEnvelope.Payload`.

3. **Integration**
   - Other agents (e.g., `CodeAgent`, `PlanAgent`) can delegate to `LLMAgent` when they need LLM help.
   - Can be registered in the actor runtime under alias `"llm"`.

---

## Example Flow
```
🧠 CodeAgent: I need to generate boilerplate code for a .NET service.
↪️ Delegates to LLMAgent with prompt: "Generate a .NET service with dependency injection"
🤖 LLMAgent → Ollama (Mistral)
💬 Returns generated C# code as response.
```

---

## Success Criteria
- LLMAgent can send prompts and receive responses from Ollama reliably.
- Response is returned to requesting agent in a standard envelope.
- Local developer can easily configure or swap the model used.

---

## Notes
- This agent is local and self-contained (no external API keys needed).
- Extendable later to support streaming, tool-use, or advanced prompt templates.
