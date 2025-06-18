# AGCTOR Code Understanding Subsystem – Staged Development Plan (TDD)

This document breaks the AGCTOR Code Understanding Subsystem into stages for incremental, test-driven development. Each stage begins with key tests and follows with the tasks needed to make those tests pass.

---

## 📦 Stage 1: Actor Hierarchy Bootstrapping (CodeGraph Core)

### ✅ Tests to Write First
- ✅ Can create `SolutionActor` and add a `ProjectActor`
- ✅ Can add multiple `FileActor`s under a project
- ✅ Can add and retrieve `ClassActor`s and `MethodActor`s
- ✅ Actors return metadata (names, paths, IDs)
- ✅ Actor state can be serialized and deserialized

### 🔨 Tasks
- Implement `IActor` base interface
- Define core actor types: `SolutionActor`, `ProjectActor`, `FileActor`, etc.
- Define messages: `AddChild`, `GetChildren`, `Summarize`, etc.
- Implement in-memory state for each actor
- Implement disk-based persistence (start with JSON)

---

## 📦 Stage 2: Language Plugin System (`ICodeAnalyzer`)

### ✅ Tests to Write First
- ✅ Can load and register multiple analyzers
- ✅ RoslynAnalyzer parses C# file and returns class + method list
- ✅ TreeSitterAnalyzer (stub) returns mock results
- ✅ LLMAnalyzer fallback responds with dummy structured output
- ✅ `FileActor` uses correct analyzer based on file extension

### 🔨 Tasks
- Define `ICodeAnalyzer` interface
- Implement `AnalyzerRegistry` with `GetAnalyzerForLanguage`
- Add `RoslynAnalyzer` (initial working implementation)
- Add placeholder/mock `TreeSitterAnalyzer`, `LLMAnalyzer`
- Wire analyzers into `FileActor` logic

---

## 📦 Stage 3: Actor Persistence System

### ✅ Tests to Write First
- ✅ Actor can save state to disk and reload from disk
- ✅ State round-trip retains actor hierarchy and metadata
- ✅ Can load partial tree (e.g., load a project without loading all files)

### 🔨 Tasks
- Design `.agctorstore/` folder schema
- Implement `SaveStateAsync` and `LoadStateAsync` for each actor
- Add persistence utilities (MessagePack or JSON config)
- Add `StorageAgent` or background save triggers

---

## 📦 Stage 4: Indexing & Embedding Layer

### ✅ Tests to Write First
- ✅ `IndexerAgent` can walk actor graph and collect code blocks
- ✅ Embeddings can be generated and stored in `EmbeddingStoreActor`
- ✅ Can query vector index and retrieve nearest neighbors
- ✅ Can persist and reload vector index

### 🔨 Tasks
- Implement `IndexerAgent` to traverse actors
- Use OpenAI embeddings or HuggingFace local model
- Implement lightweight vector store (Qdrant Embedded or HNSW.NET)
- Define `VectorSearchActor` query API
- Integrate embedding and actor IDs

---

## 📦 Stage 5: LLM Fallback Logic

### ✅ Tests to Write First
- ✅ Fallback analyzer can summarize unknown language file
- ✅ Prompts can extract method and class names from Python/Rust
- ✅ LLM can answer structural queries ("what does this function do?")

### 🔨 Tasks
- Implement `LLMAnalyzer` using local Ollama or API
- Define prompt templates for structure extraction
- Add configurable LLM fallback logic in `AnalyzerRegistry`
- Optional: chunk and context encode unsupported source files

---

## 📦 Stage 6: Code Comprehension APIs (for Agents or IDE)

### ✅ Tests to Write First
- ✅ Can query actor graph for “find all public methods”
- ✅ Can retrieve summaries from classes and methods
- ✅ Can highlight where a method is used across files
- ✅ Can search using semantic and structural filters

### 🔨 Tasks
- Define agent-to-actor query messages: `Summarize`, `FindUsage`, etc.
- Add navigation helpers: parent lookup, siblings
- Implement `Summarize` logic via AST or LLM
- Expose structured outputs for downstream agents or frontend

---

## 🔄 Optional Add-ons (After Core)
- 🔁 Snapshot diff & Git watcher agent
- 🧪 Inject test scaffolding agent
- 🧠 LLM CodeReviewer agent

---

## 🧠 Summary Table

| Stage | Focus | Tests | Tasks |
|-------|-------|-------|-------|
| 1 | Actor Graph Core | ✅ Hierarchy, structure | Define base actors + hierarchy |
| 2 | Analyzer Plugin System | ✅ Analyzer routing | Plug in analyzers per language |
| 3 | Persistence | ✅ Save/load state | Disk I/O + folder structure |
| 4 | Embedding Layer | ✅ Index & query vectors | Indexer + vector search actor |
| 5 | LLM Fallback | ✅ Answer with LLM | Language-agnostic summarization |
| 6 | Comprehension API | ✅ Ask structure queries | Agent-friendly querying API |
