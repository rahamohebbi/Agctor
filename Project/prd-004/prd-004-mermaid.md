# Agctor System – Architecture Overview

```mermaid
graph TD;
    %% Runtime layers
    subgraph "Runtime Adapter Choices (select one)"
        direction TB
        InMemoryAdapter["InMemory Adapter"]
        ProtoAdapter["Proto.Actor Adapter"]
        OrleansAdapter["Orleans Adapter (planned)"]
    end

    subgraph "Core Runtime Services"
        TaskFlowEngine["TaskFlowEngine"]
        TimeoutSupervisor["TimeoutSupervisorActor"]
        MetricsCollector["Metrics Collector"]
    end

    %% Agent hierarchy
    subgraph "Agents"
        RootAgent["RootAgent"]
        TaskScoperAgent["TaskScoperAgent"]
        LLMAgent["LLMAgent"]
        RefactorAgent["RefactorAgent"]
        SearchAgent["SearchAgent"]
        CodeReviewerAgent["CodeReviewerAgent"]
        PullRequestAgent["PullRequestAgent"]
    end

    %% Tool actors
    subgraph "Tools"
        CodeEditorTool["CodeEditorTool"]
        CodeExecutorTool["CodeExecutorTool"]
        FormatTool["FormatTool"]
    end

    %% External systems & portals
    HostAPI["HTTP + MCP Gateway"]
    CLI["CLI"]
    GitRepo["Git Repository"]
    VectorStore["Vector Store / Embeddings"]
    LLMNode["LLM (Ollama)"]
    MetricsBackend["Observability Stack (Jaeger / Prometheus / Zipkin)"]

    %% Interaction flows
    CLI --> HostAPI
    HostAPI --> RootAgent
    RootAgent --> TaskScoperAgent
    TaskScoperAgent --> TaskFlowEngine

    %% Task execution paths
    TaskFlowEngine -->|"Code-Gen Tasks"| RefactorAgent
    TaskFlowEngine -->|"PR Tasks"| PullRequestAgent

    %% Code generation pipeline
    RefactorAgent --> LLMAgent
    LLMAgent --> LLMNode
    RefactorAgent --> CodeEditorTool
    SearchAgent --> VectorStore
    SearchAgent --> LLMNode

    %% Git operations
    CodeEditorTool --> GitRepo
    PullRequestAgent --> GitRepo

    %% Code execution routes to whichever adapter is active (dashed illustrative links)
    CodeExecutorTool -.-> InMemoryAdapter
    CodeExecutorTool -.-> ProtoAdapter
    CodeExecutorTool -.-> OrleansAdapter

    %% Core services interact with the selected adapter
    InMemoryAdapter --> TaskFlowEngine
    ProtoAdapter --> TaskFlowEngine
    OrleansAdapter --> TaskFlowEngine

    %% Observability chain (from any adapter)
    InMemoryAdapter --> MetricsCollector
    ProtoAdapter --> MetricsCollector
    OrleansAdapter --> MetricsCollector
    MetricsCollector --> MetricsBackend
```

This diagram captures the high-level components and primary data flows within the Agctor framework after completion of Step 5 (Pull-Request automation). 

---

### Agctor System – Simplified View (easier to understand)

```mermaid
graph LR
    User["User / CLI / HTTP"] --> Gateway["HTTP + MCP Gateway"]
    Gateway --> Root["RootAgent"]
    Root --> Scoper["TaskScoperAgent"]
    Scoper --> Flow["TaskFlowEngine"]

    Flow -->|"Code-Gen"| Ref["RefactorAgent"]
    Flow -->|"Pull-Request"| PRA["PullRequestAgent"]

    Ref --> LLM["LLMAgent → Ollama"]
    Ref --> Editor["CodeEditorTool"]
    Editor --> Git["Git Repo"]

    PRA --> Git

    Git --> Watch["GitWatcherAgent"] --> Index["IndexerAgent"] --> Vec["Vector Store"]
    Ref --> Vec

    Git --> Snap["SnapshotService"] --> Diff["SnapshotDiffService"] --> Rev["CodeReviewerAgent"]

    %% Observability
    Flow --> Metrics["Metrics Collector"] --> Obs["Jaeger / Prometheus"]
```

---

## CodeGraph – Component Interaction Detail

```mermaid
graph LR;
    %% ==================== AGENTS ====================
    subgraph "CodeGraph Agents"
        RefactorAgentCG["RefactorAgent"]
        SearchAgentCG["SearchAgent"]
        IndexerAgentCG["IndexerAgent"]
        CodeReviewerAgentCG["CodeReviewerAgent"]
        GitWatcherAgentCG["GitWatcherAgent"]
        ComprehensionAgentCG["ComprehensionAgent"]
    end

    %% ==================== ACTOR GRAPH ====================
    subgraph "Actor Graph"
        SolutionActor["SolutionActor"] --> ProjectActor["ProjectActor"] --> FileActor["FileActor"] --> ClassActor["ClassActor"]
    end

    %% ==================== ANALYZERS ====================
    subgraph "Analyzers"
        AnalyzerRegistry["AnalyzerRegistry"]
        RoslynAnalyzer["RoslynCodeAnalyzer"]
        TreeSitterAnalyzer["TreeSitterAnalyzer (stub)"]
        LLMAnalyzer["LLMAnalyzer"]
        AnalyzerRegistry --> RoslynAnalyzer
        AnalyzerRegistry --> TreeSitterAnalyzer
        AnalyzerRegistry --> LLMAnalyzer
    end

    %% ==================== INTENT RESOLUTION ====================
    subgraph "Intent Resolvers"
        HeuristicResolver["HeuristicIntentResolver"]
        RegexResolver["RegexIntentResolver"]
        LLMIntentResolver["LlmIntentResolver"]
        ProxyResolver["ProxyIntentResolver"]
    end

    IntentDetectionAgent["IntentDetectionAgent"]
    SearchAgentCG --> IntentDetectionAgent --> HeuristicResolver
    IntentDetectionAgent --> RegexResolver
    IntentDetectionAgent --> LLMIntentResolver
    IntentDetectionAgent --> ProxyResolver

    %% ==================== EMBEDDINGS & SEARCH ====================
    subgraph "Embeddings"
        EmbeddingGenerator["IEmbeddingGenerator"]
        VectorStore["InMemoryVectorStore"]
        IndexerAgentCG --> EmbeddingGenerator --> VectorStore
        SearchAgentCG --> VectorStore
    end

    %% ==================== SNIPPETS & LLM ====================
    SnippetProvider["SnippetProviderRegistry"]
    LLMClient["ILlmClient (Ollama)"]
    SearchAgentCG --> SnippetProvider
    RefactorAgentCG --> LLMClient

    %% ==================== SERVICES ====================
    subgraph "Services"
        DiffFormatter["DiffFormatterService"]
        SnapshotService["SnapshotService"]
        SnapshotDiff["SnapshotDiffService"]
    end

    %% ==================== TOOLS ====================
    CodeEditorToolCG["CodeEditorTool"]

    %% ======== FLOWS ========
    RefactorAgentCG --> SearchAgentCG
    RefactorAgentCG --> CodeEditorToolCG

    GitWatcherAgentCG --> SnapshotService --> SnapshotDiff --> CodeReviewerAgentCG
    CodeReviewerAgentCG --> DiffFormatter

    ComprehensionAgentCG --> SolutionActor
    SearchAgentCG --> SolutionActor

    %% Messaging relationships (dashed)
    RefactorAgentCG -. "context request" .-> AnalyzerRegistry
    AnalyzerRegistry -. "analysis results" .-> RefactorAgentCG
```

The diagram illustrates how the main CodeGraph agents, analyzers, actor graph, embedding pipeline, and services collaborate to process code-understanding and refactor commands. 

---

### CodeGraph – Simplified View (easier to understand)

```mermaid
graph TD
    %% High-level pipeline
    Start["↪ Command (natural-language)"] --> SA["SearchAgent"]
    SA -->|"detect intent"| IntentDetect["IntentDetectionAgent"]
    IntentDetect -->|"intent + context"| Refactor["RefactorAgent"]
    Refactor -->|"ask LLM"| LLM["LLMAgent → Ollama"]
    Refactor -->|"apply patch"| Editor["CodeEditorTool"]
    Editor --> Git["Git Repository"]

    %% Feedback loop
    Git --> Watcher["GitWatcherAgent"] --> Indexer["IndexerAgent"] --> VectorStore["Vector Store"]
    SA --> VectorStore

    %% Review flow
    Git --> Snapshot["SnapshotService"] --> Diff["SnapshotDiffService"] --> Reviewer["CodeReviewerAgent"]
```

This view groups the many components into a concise end-to-end flow:
1. SearchAgent interprets a user command and resolves intent.
2. RefactorAgent fetches context, consults the LLM, and edits code through CodeEditorTool.
3. Git changes trigger indexing and review agents, closing the feedback loop. 