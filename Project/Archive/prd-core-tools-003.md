# 🛠️ PRD: Tool Usage in Agctor

## Goal
Enable Agctor Agents to **use external Tools** in a flexible and composable way. Each Tool is modeled as an **Actor**, allowing agents to call tools just like they would call other agents.

## Why We're Doing This
Real-world agent workflows require actions like running code, testing, and working with files — all without user interfaces. Modeling tools as actors lets us plug them into the actor system and treat them the same as any other agent.

## Core Concepts

### 1. Tool is an Actor
Each tool (like "Run Code", "Format Code", "Search Files") is a special kind of actor that handles a `ToolRequest` and returns a `ToolResult`.

### 2. Agents Use Tools via Messaging
Agents send standard `MessageEnvelope` messages to tool actors. The tool processes the input and returns the result to the sender.

### 3. Chaining and Composition
To enable powerful workflows, Agents must be able to orchestrate tools in complex ways:
- **Sequential Chaining:** Execute a series of tools where the output of one becomes the input for the next (`Tool1_Output → Tool2_Input`).
- **Conditional Logic:** Use the result of a tool to decide which tool to run next.
- **Fan-out/Fan-in:** Send a task to multiple tools in parallel and then aggregate their results for a final answer.

--- 

## Agent Requirements for Effective Tool Use

For tool chaining to be possible, the core `Agent` logic must be enhanced. The current implementation lacks the necessary mechanisms for sequential execution and data forwarding between subtasks, as revealed by integration testing.

### ✅ Subtask Orchestration and Planning
The supervising `Agent` must be able to create and execute a multi-step **plan**. Instead of just generating a simple list of subtasks to be run in parallel, it needs to:
1.  **Define a sequence:** Understand that some tasks must happen before others.
2.  **Manage State:** Keep track of the overall plan's execution state (e.g., which step is next, what data has been collected).

### ✅ Data-Forwarding Between Subtasks
The `Agent` must implement a mechanism to **forward the output from a completed subtask as the input to the next subtask** in the plan.
- **Example Workflow:**
    1.  **User Prompt:** "Write a hello world app and save it to `program.cs`."
    2.  **Agent Plan:**
        - **Step 1:** Call `LLMAgent` with the prompt "write a hello world c# console application".
        - **Step 2:** Take the code output from Step 1 and call `CodeEditorTool` with the operation `WriteFile`, path `program.cs`, and the generated code as content.
    3.  **Execution:** The `Agent` waits for `LLMAgent` to return the code, then constructs the `ToolRequest` for `CodeEditorTool` using that code.

This capability is critical for fulfilling any non-trivial user request that involves multiple tools.

### Implementation Challenges

Integration testing revealed several architectural limitations in the current agent implementation that prevent effective tool chaining:

1. **Message Routing Issues:** 
   - Completion messages from child agents/tools don't always reach the parent agent
   - The parent agent needs robust mechanisms to process and act on these completion messages

2. **State Management:**
   - The `Agent` class needs to maintain state between subtask executions
   - The `_lastSubtaskResult` and `_subtaskResults` dictionary help track outputs from previous steps

3. **Tool Interface Design:**
   - Tools inherit from `Agent` but also implement `IToolActor`
   - This dual inheritance requires careful implementation of method overrides

4. **Sequential Execution:**
   - The `ProcessSubtaskResultAsync` method now contains logic to:
     - Process the result of a completed subtask
     - Store it for use in subsequent steps
     - Continue to the next subtask in the queue

These architectural enhancements create a small workflow engine within the Agent class, enabling it to execute multi-step plans where the output of one step becomes the input for the next.

---

## Important Coding Tools to Implement First

| Tool Name            | Description                                                                 |
|----------------------|-----------------------------------------------------------------------------|
| `CodeExecutorTool`   | Run code snippets (C#, Python, etc.) and return output/errors              |
| `UnitTestRunnerTool` | Execute unit tests and report results                                      |
| `CodeFormatterTool`  | Format source files using appropriate linters/formatters                  |
| `CodeLinterTool`     | Perform static analysis to find errors or warnings                         |
| `GitTool`            | Perform Git actions like `clone`, `diff`, `commit`, `log`                 |
| `PromptStoreTool`    | Store prompt + result history in Git-backed folder                        |
| `DiffTool`           | Compare two versions of a file or function and return a semantic diff      |
| `FileSystemTool`     | Read from or write to disk, create folders, delete files                   |
| `ErrorExplainerTool` | Convert error output into natural-language explanations                   |

Each tool implements `IToolActor`.

---

# Agent Architecture: Tool Chaining for Complex Workflows

## Overview

This PRD outlines the requirements for extending the Agctor SDK to support sequential tool chaining for complex workflows. The architecture should enable agents to decompose a complex prompt into a sequence of steps, delegate each step to an appropriate tool or child agent, and orchestrate the execution of these steps in a specific order while passing data between them.

## Requirements

### 1. Prompt Decomposition and Task Planning

- Agents should be able to analyze a complex prompt and break it down into a sequence of steps
- Each step should be assigned to a specific tool or agent type best suited for handling that task
- The decomposition should identify dependencies between steps and establish a correct execution order

### 2. Sequential Execution

- Agents should execute subtasks in a sequential order, respecting dependencies
- A step should only begin execution after its prerequisite steps have completed
- The supervisor agent should maintain the execution state and control the flow

### 3. Data Forwarding

- Results from one step should be available as input to subsequent steps
- Data should be transformed into the appropriate format required by each tool
- The agent should handle different output formats from various tools and convert them as needed

### 4. Error Handling

- If a step fails, the agent should be able to:
  - Retry the step with modified parameters
  - Skip the step if possible
  - Abort the entire workflow if the step is critical
  - Provide meaningful error messages to the user

### 5. Debugging and Monitoring

- Provide detailed logging of the execution flow
- Enable tracing of data as it moves between steps
- Allow inspection of the execution state at any point

## Implementation Approach

The implementation follows these design principles:

1. Extend the `Agent` class to support workflow orchestration
2. Use a queue-based approach for sequential execution
3. Store subtask results for use in later steps
4. Implement a dynamic agent type selection based on task requirements
5. Provide feedback mechanisms for reporting progress and errors

## Implementation Details

### Tool Interface

```csharp
public interface IToolActor : IActor {
    Task<ToolResult> Handle(ToolRequest request);
}
```

### Tool Result Structure
```csharp
public class ToolResult
{
    public bool IsSuccess { get; set; }
    public object Output { get; set; }
    public string Error { get; set; }
}
```

### Tool Request Structure
```csharp
public class ToolRequest
{
    public string Operation { get; set; }
    public Dictionary<string, object> Parameters { get; set; }
}
```

### Code Editor Tool Implementation
The `CodeEditorTool` provides file operations like writing, inserting, and replacing content in files:

```csharp
public class CodeEditorTool : Agent, IToolActor
{
    private readonly IFileSystem _fileSystem;
    
    // ... constructor and other methods ...
    
    public virtual async Task<ToolResult> Handle(ToolRequest request)
    {
        try
        {
            return request.Operation switch
            {
                "WriteFile" => await WriteFile(request.Parameters),
                "InsertIntoFile" => await InsertIntoFile(request.Parameters),
                "ReplaceInFile" => await ReplaceInFile(request.Parameters),
                _ => new ToolResult { IsSuccess = false, Error = $"Unsupported operation: {request.Operation}" }
            };
        }
        catch (Exception ex)
        {
            return new ToolResult { IsSuccess = false, Error = ex.Message };
        }
    }
    
    // ... implementation of file operations ...
}
```

### Agent Enhancements

1. **Subtask Queue**: Added a queue-based system in the Agent class to manage sequential execution of subtasks
2. **Task Decomposition**: Enhanced the Agent's ability to decompose complex prompts into logical steps
3. **Result Storage**: Added mechanisms to store and forward results between sequential steps
4. **Dynamic Agent Selection**: Implemented intelligence to select appropriate agent types based on task semantics
5. **Error Propagation**: Improved error handling and propagation to allow recovery from failures

### Tool Interface Improvements

1. **Structured Input**: Enhanced tools to accept complex structured input via parameters
2. **Command Parsing**: Improved parsing of tool commands for better flexibility
3. **File Operations**: Added robust file handling capabilities with proper error reporting
4. **Tool Result Format**: Standardized tool result format to facilitate integration

## Integration Testing Strategy

Testing the end-to-end workflow capabilities requires verifying:

1. **Subtask Decomposition**: The agent properly breaks down complex prompts into logical subtasks
2. **Agent Selection**: The right agent type is selected for each subtask
3. **Sequential Execution**: Subtasks execute in the correct order
4. **Data Forwarding**: Data flows correctly between subtasks
5. **Error Handling**: The system handles errors gracefully

For testing purposes, we've implemented:

1. **TestLLMAgent**: A specialized LLM agent that returns predefined responses for testing
2. **TestCodeEditorTool**: A tool agent that can verify file operations using a mock filesystem
3. **TestAgentFactory**: A factory that can create test-specific agent instances

### Testing Challenges and Solutions

During implementation and testing, we encountered several architectural challenges:

1. **Message Routing**: The message-passing architecture made it difficult to reliably track the flow of execution between agents.
   - **Solution**: Added direct initialization and processing in the test agent factory to avoid relying solely on message passing.

2. **State Management**: Maintaining state between subtasks proved challenging in the asynchronous environment.
   - **Solution**: Enhanced the Agent class to store the last subtask result and pass it to subsequent steps.

3. **Tool Interface Design**: The original design made it difficult for tools to receive complex structured input.
   - **Solution**: Improved the code editor tool to accept code content via parameters and enhanced command parsing.

4. **Asynchronous Testing**: Verifying asynchronous workflows in unit tests is inherently challenging.
   - **Solution**: Used a combination of mock objects, callbacks, and direct verification to ensure test reliability.

### Testing Infrastructure

1. **Mock Objects**: Created mock implementations of key system components
2. **Test Agents**: Developed specialized test agents with predictable behavior
3. **Diagnostics**: Added comprehensive logging throughout the system for better debugging
4. **Test Isolation**: Ensured tests do not interfere with each other and can run independently

## End-to-End Test Case

The primary test case validates that:

1. A user can request "write a hello world c# console application and save it to a file named 'program.cs'"
2. The system decomposes this into two subtasks:
   - Generate C# hello world code (handled by LLMAgent)
   - Save the generated code to a file (handled by CodeEditorTool)
3. The code is correctly saved to the specified file

This test validates the core requirements of subtask decomposition, sequential execution, and data forwarding between steps.

## Future Considerations

1. **Parallel Execution**: For independent subtasks, adding parallel execution capabilities
2. **User Interaction**: Supporting workflows that require intermediate user input or confirmation
3. **Long-Running Tasks**: Handling steps that may take extended periods to complete
4. **Persistent Workflows**: Allowing workflows to be saved, paused, and resumed

## Conclusion

The enhanced agent architecture now provides a solid foundation for building complex, multi-step workflows. Agents can intelligently decompose tasks, select appropriate tools, execute steps in sequence, and pass data between steps. This capability enables the creation of much more powerful and flexible AI assistants that can handle real-world tasks requiring multiple specialized tools working together.

While the current implementation focuses on sequential execution with direct data passing, the architecture has been designed to accommodate future enhancements such as parallel execution, branching workflows, user interaction, and persistent workflow state.