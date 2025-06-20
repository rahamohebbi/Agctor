# AGCTOR Code Understanding Subsystem – Integration Testing Plan

This document details the integration testing strategy for the AGCTOR Code Understanding Subsystem. These tests simulate real workflows across actors, analyzers, vector search, LLMs, and persistence.

---

## 🔗 Integration Test Groups and Scenarios

---

### 🔄 Group 1: Codebase Initialization & Graph Creation

#### ✅ Test: `LoadSolutionFromDisk_ShouldCreateCompleteActorGraph`
- **Setup**: A pre-populated `.agctorstore/` containing a multi-project solution.
- **Expectations**:
  - Actor graph is reconstructed correctly (`SolutionActor → ProjectActor → FileActor → ClassActor → MethodActor`)
  - All node metadata (name, type, location, ID) match source
  - File content matches the disk
  - Parent-child relationships remain intact

---

### 🧠 Group 2: Multi-Language Analysis

#### ✅ Test: `AnalyzeMixedLanguageProject_ShouldUseCorrectAnalyzers`
- **Setup**: A sample repo with `.cs`, `.py`, `.rs`, and `.foo` files.
- **Expectations**:
  - `.cs` → parsed by RoslynAnalyzer
  - `.py` → parsed by TreeSitterAnalyzer
  - `.rs`, `.foo` → handled by LLMAnalyzer
  - All analyzers return consistent `ParsedFile` objects with structured metadata
  - Actor tree reflects correct file-language mapping

#### ✅ Test: `LLMAnalyzer_ShouldProduceFallbackSummary`
- **Setup**: File in an unsupported language
- **Expectations**:
  - Class/function names correctly inferred
  - Method parameters and return types extracted via LLM
  - Returned structure usable by IndexerAgent and scaffolding tools

---

### 🧠 Group 3: Embedding & Vector Search

#### ✅ Test: `IndexerAgent_ShouldGenerateAndStoreEmbeddings`
- **Setup**: Empty vector store, actor graph loaded
- **Expectations**:
  - IndexerAgent collects method-level source from actors
  - Embeddings are created and stored with metadata
  - Disk-based vector store updated
  - Embeddings linked to actor IDs

#### ✅ Test: `VectorSearchActor_ShouldReturnSemanticMatches`
- **Query**: "user login logic"
- **Expectations**:
  - Top-K method or class actors returned
  - Results include cosine similarity scores
  - Associated file and method context is attached

---

### 🔁 Group 4: Snapshot & Diff Detection

#### ✅ Test: `GitWatcherAgent_ShouldTriggerSnapshotDiff`
- **Setup**: Simulated Git commit changing a method body
- **Expectations**:
  - Snapshot taken before and after change
  - `DiffService` identifies structural delta
  - Changed actors flagged
  - Embedding drift calculated and optionally re-indexed

---

### 🧪 Group 5: Test Scaffolding Flow

#### ✅ Test: `InjectTest_ShouldCreateTestTemplate`
- **Input**: Target method like `RegisterUser()`
- **Expectations**:
  - Test file is created or updated
  - Test function uses correct structure and naming
  - Uses mock/stub data for inputs
  - Output is compilable and persists to disk

---

### 🧠 Group 6: LLM Code Review

#### ✅ Test: `CodeReviewerAgent_ShouldSummarizeDiffAndScore`
- **Input**: Code diff or PR-style input
- **Expectations**:
  - Human-readable summary of changes
  - Comments on test coverage, duplication, complexity
  - Scorecard output with categories like "readability", "risk level", "coverage completeness"

---

### 🤖 Group 7: Intent Detection & Natural-Language Search (NEW)

#### ✅ Test: `IntentDetectionAgent_ShouldResolveQueryViaLlm`
- **Setup**: Query "Which classes implement ICalculator?" – not matched by heuristics.
- **Expectations**:
  - All local resolvers return `null`.
  - `IntentDetectionAgent` is invoked and returns `IntentKind.SemanticSearch`.
  - `SearchAgent` executes semantic flow and returns matching classes.

#### ✅ Test: `HeuristicIntentResolver_ShouldHandleListCommands`
- **Query**: "list classes"
- **Expectations**:
  - Heuristic resolver detects `IntentKind.ListClasses` with high confidence.
  - No LLM calls are made (verified via mock `ILlmClient`).

#### ✅ Test: `SearchAgent_ShouldFallbackToVectorSearch`
- **Query**: "parses json"
- **Expectations**:
  - No resolver matches confidently.
  - Vector search is invoked with query text.
  - Results include methods/classes with JSON parsing.

#### ✅ Test: `QueryAgent_ShouldReformatOnly`
- **Setup**: Structural answer already contains desired code snippet.
- **Expectations**:
  - LLM prompt instructs model to *reformat without inventing new code*.
  - Response contains only re-formatted snippet, no hallucinations.

---

### ✅ End-to-End Scenario

#### ✅ Test: `SelfImprovingAgent_ShouldUnderstandIndexAndSuggestPR`
- **Input**: High-level goal (e.g., "Add email verification to registration logic")
- **Expected Flow**:
  - PlannerAgent analyzes goal → identifies affected actors
  - VectorSearchActor narrows candidate classes/methods
  - TestPlannerAgent injects failing tests
  - CodeWriterAgent writes logic to pass them
  - CodeReviewAgent evaluates change
  - PRAgent creates PR with:
    - Test list
    - Change explanation
    - Optional diagrams

---

## ✅ Notes
- Integration tests can be run inside a Docker container or test harness
- Fixture projects should include:
  - Multiple languages
  - Auth, controller, and data flow logic
  - Unit/integration test files
