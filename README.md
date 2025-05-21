# AGCTOR
 AGCTOR = Agentic Actor Model

```
+---------------------+
|     Agent Logic     |   <-- Your AI/agent code (business logic)
|  (IAgent interface) |
+----------+----------+
           |
           v
+---------------------+
|   Actor Abstraction |   <-- Unified Actor runtime interface
| (IActorRuntime, etc)|
+----------+----------+
           |
   +-------+-------+--------------------------+
   |               |                          |
   v               v                          v
+----------+   +------------+           +------------+
| Orleans  |   | Proto.Actor|           | InMemory   |   <-- Backends
| Adapter  |   | Adapter    |           | Adapter    |
+----------+   +------------+           +------------+
```


An Agentic framework where:  
- Agents (actors) are logic units.
- Actor model backends (e.g., Orleans, Proto.Actor, Akka.NET, in-process actors, WASM actors) are swappable.
- Your system offers a stable interface (IAgent, IActorRuntime) and runtime-agnostic messaging.