# Agctor System Architecture Diagram

This diagram provides a comprehensive overview of the Agctor system, grouping components logically into Actors, Agents, Adapters, Tools, Core Services, and External Systems. Arrows indicate message passing and communication flows.

```mermaid
graph TD;
    %% Define subgraphs for logical grouping

    subgraph "Actors (Core Building Blocks)"
        IActor["IActor Interface\n- Id\n- ActorType\n- State\n- ReceiveAsync\n- InitializeAsync\n- ShutdownAsync"]
        CodeGraphActorBase["CodeGraphActorBase\n- Base for hierarchical actors\n- Manages children\n- State management"]
        TimeoutSupervisorActor["TimeoutSupervisorActor\n- Monitors timeouts\n- Supervises other actors"]
        EchoActor["EchoActor\n- Simple demo actor\n- Echoes messages"]
    end

    subgraph "Agents (Intelligent Entities extending IActor)"
        IAgent["IAgent Interface\n- Extends IActor\n- CurrentPrompt\n- ParentAgentId\n- ChildAgentIds\n- Status\n- ProcessPromptAsync"]
        Agent["Agent\n- Basic agent implementation\n- Spawns child agents\n- Processes prompts recursively"]
        EchoAgent["EchoAgent\n- Simple echoing agent"]
        LLMAgent["LLMAgent\n- Integrates with LLM\n- Processes AI tasks"]
        RefactorAgent["RefactorAgent\n- Handles code refactoring"]
        SearchAgent["SearchAgent\n- Performs searches\n- Uses embeddings"]
        CodeReviewerAgent["CodeReviewerAgent\n- Reviews code changes"]
        PullRequestAgent["PullRequestAgent\n- Manages PR tasks"]
        GitWatcherAgent["GitWatcherAgent\n- Monitors Git events"]
        IndexerAgent["IndexerAgent\n- Indexes code for search"]
        IntentDetectionAgent["IntentDetectionAgent\n- Detects user intents"]
        ComprehensionAgent["ComprehensionAgent\n- Code comprehension"]
    end

    subgraph "Adapters (Runtime Backends)"
        IActorRuntimeAdapter["IActorRuntimeAdapter\n- Name\n- Version\n- IsInitialized\n- InitializeAsync\n- ShutdownAsync\n- SpawnActorAsync\n- SendMessageAsync"]
        InMemoryActorRuntime["InMemoryActorRuntime\n- In-memory execution"]
        OrleansAdapter["OrleansAdapter\n- Distributed actor model"]
        ProtoActorAdapter["ProtoActorAdapter\n- Proto.Actor integration"]
    end

    subgraph "Tools (Specialized Actors for Tasks)"
        IToolActor["IToolActor\n- Extends IAgent\n- Handles ToolRequest"]
        CodeEditorTool["CodeEditorTool\n- Edits code files"]
        CodeExecutorTool["CodeExecutorTool\n- Executes code snippets"]
        FormatTool["FormatTool\n- Formats code"]
        FileSystemTool["FileSystemTool\n- File operations"]
        CompileTool["CompileTool\n- Compiles code"]
    end

    subgraph "Task Executors"
        ITaskExecutor["ITaskExecutor\n- ExecuteAsync"]
        CoderTaskExecutor["CoderTaskExecutor\n- Code generation tasks"]
        PullRequestTaskExecutor["PullRequestTaskExecutor\n- PR-related tasks"]
        CodeGraphTaskExecutor["CodeGraphTaskExecutor\n- Code graph operations"]
    end

    subgraph "Core Services"
        MessageDispatcher["MessageDispatcher\n- Routes messages to agents"]
        AgentRegistry["AgentRegistry\n- Tracks all agents"]
        TaskFlowEngine["TaskFlowEngine\n- Orchestrates task flows"]
        AnalyzerRegistry["AnalyzerRegistry\n- Manages code analyzers (Roslyn, TreeSitter, LLM)"]
        SnapshotService["SnapshotService\n- Manages code snapshots"]
        SnapshotDiffService["SnapshotDiffService\n- Computes diffs"]
        EmbeddingStore["EmbeddingStore\n- Stores vector embeddings"]
    end

    subgraph "Messages (Communication)"
        IMessageEnvelope["IMessageEnvelope\n- Id\n- Payload\n- Headers\n- SenderId\n- TargetId"]
        ProcessPromptMessage["ProcessPromptMessage\n- Prompt\n- CorrelationId"]
        AssignSubtaskMessage["AssignSubtaskMessage\n- Subtask prompt"]
        ToolRequest["ToolRequest\n- Tool-specific params"]
        ToolResult["ToolResult\n- Success\n- Data\n- Error"]
    end

    subgraph "External Systems"
        CLI["CLI\n- User interface"]
        HostAPI["Host API\n- HTTP + MCP Gateway"]
        GitRepo["Git Repository"]
        LLM["LLM (e.g., Ollama)"]
        VectorStore["Vector Store"]
        MetricsBackend["Metrics/Observability"]
    end

    %% Communication Flows (Message Passing)
    CLI -->|Sends commands| HostAPI
    HostAPI -->|Dispatches messages| MessageDispatcher
    MessageDispatcher -->|Routes to| Agent
    Agent -->|Spawns children| LLMAgent
    Agent -->|Spawns children| RefactorAgent
    Agent -->|Spawns children| SearchAgent
    RefactorAgent -->|Uses| CodeEditorTool
    RefactorAgent -->|Uses| CodeExecutorTool
    SearchAgent -->|Queries| EmbeddingStore
    SearchAgent -->|Queries| VectorStore
    LLMAgent -->|Calls| LLM
    CodeReviewerAgent -->|Analyzes| AnalyzerRegistry
    PullRequestAgent -->|Interacts with| GitRepo
    GitWatcherAgent -->|Monitors| GitRepo
    IndexerAgent -->|Updates| EmbeddingStore
    IntentDetectionAgent -->|Detects intents| ComprehensionAgent

    %% Adapter Integration
    IActorRuntimeAdapter <-->|Implemented by| InMemoryActorRuntime
    IActorRuntimeAdapter <-->|Implemented by| OrleansAdapter
    IActorRuntimeAdapter <-->|Implemented by| ProtoActorAdapter
    Agent -->|Runs on| IActorRuntimeAdapter
    IToolActor -->|Runs on| IActorRuntimeAdapter

    %% Message Passing Examples
    Agent -->|Sends ProcessPromptMessage| LLMAgent
    LLMAgent -->|Returns ToolResult| Agent
    Agent -->|Sends ToolRequest| CodeEditorTool
    CodeEditorTool -->|Returns ToolResult| Agent

    %% Core Interactions
    TaskFlowEngine -->|Executes| CoderTaskExecutor
    TaskFlowEngine -->|Executes| PullRequestTaskExecutor
    SnapshotService -->|Uses| SnapshotDiffService
    MetricsCollector -->|Sends to| MetricsBackend

    %% Styles for better visualization
    classDef interface fill:#f9f,stroke:#333,stroke-width:2px;
    class IActor,IAgent,IActorRuntimeAdapter,IToolActor,ITaskExecutor,IMessageEnvelope interface;
    classDef message fill:#ddf,stroke:#333;
    class ProcessPromptMessage,AssignSubtaskMessage,ToolRequest,ToolResult message;
