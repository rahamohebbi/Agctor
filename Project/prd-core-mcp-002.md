# PRD: MCP Compatibility for All Agents

## Feature Name
MCP (Model Context Protocol) Compliance for All Agents

---

## Objective
Ensure all agents in the Agctor system are compatible with the Model Context Protocol (MCP), which standardizes message format, context, and execution flow across agents.

---

## User Story
> As a system integrator, I want every agent to understand and process messages in a standardized MCP envelope so that they can interoperate regardless of their backend or task type.

---

## Requirements
1. **Standard Envelope**
   - Every agent must accept and return `MessageEnvelope` objects that conform to MCP spec.
   - Fields include:
     - `Id`: Unique ID for traceability
     - `Payload`: Serialized task input/output (can be JSON, Protobuf, etc.)
     - `Metadata`: Optional key-value context
     - `Headers`: Routing, agent type, content type

2. **Serialization Format**
   - Default to JSON.
   - Allow future support for Protobuf and binary formats via strategy pattern.

3. **Envelope Handling**
   - Agent logic must be decoupled from raw payload.
   - Agents must extract context (e.g., task type, agent origin) from `Headers` or `Metadata`.

4. **Validation**
   - Validate incoming envelopes against MCP schema.
   - Log or reject if malformed.

5. **Adapters and Gateways**
   - Actor backends must route messages in MCP format.
   - CLI and external APIs must wrap/unwrap MCP envelopes when sending/receiving.

---

## Example Envelope
```json
{
  "id": "abc-123",
  "payload": "Generate a REST API in Go",
  "metadata": {
    "priority": "high",
    "language": "go"
  },
  "headers": {
    "content-type": "text/plain",
    "agent": "coder",
    "reply-to": "llm"
  }
}
```

---

## Success Criteria
- All agents can send, receive, and reason over MCP-formatted messages.
- Message format is consistent across runtimes and backends.
- Agents remain swappable and interoperable via the MCP contract.

---

## Notes
- This lays the foundation for future cross-platform agent orchestration.
- Testing should cover serialization, deserialization, and envelope validation.
