# Product Requirements Document: Agctor CLI Agent Runner

## 1. Introduction

*   **Product Name:** Agctor CLI Agent Runner
*   **Purpose:** A command-line interface (CLI) designed for processing user-defined prompts through the Agctor agent system. The tool accepts prompts directly via command-line arguments, dispatches them to a root agent for processing, and then outputs the results to the console. It serves as a simple and direct way to interact with and test the Agctor agent framework.

## 2. Goals

*   To provide a straightforward command-line utility for users to submit prompts to the Agctor agent system.
*   To enable quick testing and validation of agent behaviors and the Agctor framework itself.
*   To display processing status and results directly in the console for immediate feedback.
*   To support configurable Agctor runtimes, starting with an "InMemory" default and allowing for future expansion to other runtimes like Orleans or Proto.Actor.
*   To ensure ease of use for developers and testers of the Agctor SDK.

## 3. Target Audience

*   **Primary:** Developers building and maintaining the Agctor SDK and agent-based applications.
*   **Secondary:** QA engineers testing the Agctor framework and agent functionalities.
*   **Tertiary:** Users who require a lightweight, non-UI method for quick prompt processing using the Agctor system.

## 4. Features (Functional Requirements)

### F1: Command-Line Argument Processing
*   **F1.1:** The CLI must accept a mandatory textual prompt as the first command-line argument.
    *   _Comment:_ This prompt is the primary input for the agent system.
*   **F1.2:** If the prompt argument is missing or empty, the CLI must display usage instructions and terminate gracefully.
*   **F1.3:** The CLI must accept an optional runtime name as the second command-line argument.
    *   _Comment:_ This allows selection of different backend actor system implementations.
*   **F1.4:** If the runtime name is not provided, the CLI must default to using the "InMemory" runtime.
*   **F1.5:** The CLI must validate the availability of the specified runtime. If unavailable, it should display an error message listing available runtimes and terminate.

### F2: Initialization and Configuration
*   **F2.1:** Display a startup message identifying the tool (e.g., "🤖 Agctor CLI Agent Runner").
*   **F2.2:** Display the received prompt and the selected (or defaulted) runtime.
*   **F2.3:** Configure and build a dependency injection (DI) container.
    *   _Comment:_ Manages services like logging and Agctor components.
*   **F2.4:** Register console logging services with a minimum logging level of `Information`.
*   **F2.5:** Register Agctor services with the DI container, configuring:
    *   `DefaultRuntime`: Set to the selected runtime.
    *   `MaxConcurrentMessages`: Configurable (e.g., 100).
    *   `EnableDetailedLogging`: Set to `false` for CLI simplicity.
    *   `Environment`: Set to "CLI".
*   **F2.6:** Initialize the selected `IActorRuntimeAdapter`.
    *   _Comment:_ The runtime adapter handles communication with the underlying actor system.
*   **F2.7:** Configure the runtime adapter with CLI-specific settings:
    *   `Environment`: "CLI".
    *   `MaxConcurrentMessages`: Configurable (e.g., 50).
    *   `EnableMetrics`: Set to `false` to minimize overhead.
*   **F2.8:** Log runtime initialization progress and success.

### F3: Agent-Based Prompt Processing
*   **F3.1:** Create an `AgentFactory` using the initialized runtime.
*   **F3.2:** Spawn a root `Agent` instance to handle the user's prompt.
    *   _Comment:_ This agent orchestrates the processing of the given prompt.
*   **F3.3:** Assign a unique identifier to the root agent (e.g., `cli-root-{Guid}`).
*   **F3.4:** Pass the user prompt to the root agent for processing.
*   **F3.5:** Log the creation of the root agent and the initiation of prompt processing.

### F4: Monitoring and Feedback
*   **F4.1:** Continuously monitor the status of the root agent.
    *   _Comment:_ Polling occurs at defined intervals (e.g., 500ms).
*   **F4.2:** Implement a configurable timeout for the prompt processing operation (e.g., 5 minutes).
*   **F4.3:** Periodically log the agent's processing status, including current status and the number of child agents, for long-running operations (e.g., every 10 seconds).

### F5: Result Output and Termination
*   **F5.1:** Upon successful completion (`AgentStatus.Completed`):
    *   Log successful completion.
    *   Display a success message to the console, including the root agent's ID and the count of any child agents spawned.
    *   _Comment:_ Currently returns a summary; future enhancement could return specific agent result data.
*   **F5.2:** If processing fails (`AgentStatus.Failed`):
    *   Log the failure.
    *   Display an error message indicating the failure.
*   **F5.3:** If processing times out:
    *   Log the timeout event.
    *   Display a timeout message, including the last known agent status.
*   **F5.4:** Perform resource cleanup:
    *   Gracefully shut down the actor runtime.
    *   Dispose of the runtime adapter.
    *   Dispose of the DI service provider.

### F6: Error Handling
*   **F6.1:** Implement top-level exception handling to catch any unhandled errors during execution.
*   **F6.2:** Upon catching an unhandled exception, display a user-friendly error message.
*   **F6.3:** Set the process exit code to a non-zero value (e.g., 1) to indicate an error occurred.

### F7: Usage Information
*   **F7.1:** Provide a `ShowUsage` function that displays:
    *   The tool's name.
    *   Correct command-line syntax: `AgctorCLI.exe "Your prompt here" [runtime]`.
    *   Description of arguments (`prompt`, `runtime`).
    *   Illustrative examples of usage.
    *   A list of available runtimes (e.g., "InMemory", "Orleans", "Proto.Actor"), with a note about implementation status if applicable.

## 5. Non-Functional Requirements

*   **NFR1: Performance:**
    *   The CLI application should initialize quickly.
    *   Agent status polling should be efficient (current: 500ms interval).
    *   Processing timeout (current: 5 minutes) should be adequate for typical CLI use cases but also prevent indefinite hangs.
*   **NFR2: Usability:**
    *   Command-line arguments must be clear and intuitive.
    *   Console output must be informative, providing clear feedback on progress, success, or failure.
    *   Error messages must be user-friendly and actionable where possible.
    *   Comprehensive usage instructions must be easily accessible.
*   **NFR3: Reliability:**
    *   The application must handle errors gracefully and terminate without crashing.
    *   It must ensure proper cleanup of resources (e.g., runtime, DI container) on exit.
*   **NFR4: Extensibility:**
    *   The system should be designed to easily incorporate support for additional Agctor runtimes via the `IActorRuntimeAdapterFactory`.
    *   Code should be modular (e.g., separate methods for DI, runtime init, prompt processing) to facilitate maintenance and future enhancements.
*   **NFR5: Logging:**
    *   Sufficient logging (at `Information` level) must be implemented to trace execution flow and diagnose issues.

## 6. Command-Line Interface (CLI) Specification

*   **Command:** `AgctorCLI.exe`
*   **Arguments:**
    1.  `"<prompt>"` (string, required): The prompt or task to be processed by the Agctor agent system. Must be enclosed in quotes if it contains spaces.
    2.  `[runtime_name]` (string, optional): The name of the Agctor runtime to be used.
        *   Defaults to: `"InMemory"`
        *   Currently mentioned available runtimes: `"InMemory"`, `"Orleans"`, `"Proto.Actor"`.
        *   _Comment:_ The implementation status of runtimes other than "InMemory" should be clearly communicated.

## 7. Error Handling Summary

*   **Invalid Arguments (e.g., missing prompt):** Display usage information and exit.
*   **Runtime Not Available:** Display an error message listing valid runtimes and exit.
*   **Agent Processing Failure:** Display a specific error message related to prompt processing failure.
*   **Agent Processing Timeout:** Display a message indicating that the operation timed out.
*   **General/Unhandled Exceptions:** Display a generic error message and set a non-zero exit code.

## 8. Dependencies

*   .NET Core / .NET Standard compatible
*   `Microsoft.Extensions.DependencyInjection`
*   `Microsoft.Extensions.Logging` (specifically `Microsoft.Extensions.Logging.Console`)
*   `AgctorSDK.Core` (including `AgctorSDK.Core.Interfaces`, `AgctorSDK.Core.DependencyInjection`, `AgctorSDK.Core.Agents`)

## 9. Future Considerations / Potential Enhancements

*   **Rich Agent Results:** Modify the CLI to retrieve and display the actual structured result or payload from the root agent, rather than just a summary message.
*   **Advanced Progress Reporting:** Implement more detailed progress indicators, potentially based on feedback from the agent system if available (e.g., percentage completion, steps processed).
*   **Configuration File:** Introduce support for a configuration file (e.g., JSON, XML) to manage settings like default runtime, timeouts, logging levels, and `MaxConcurrentMessages`.
*   **Interactive Mode:** Add an option for an interactive mode where users can enter multiple prompts sequentially without restarting the CLI.
*   **Detailed Agent Error Reporting:** Enhance error messages to include more specific details from agent failures or exceptions within the agent system.
*   **Full Runtime Support:** Complete and thoroughly test implementations for other advertised runtimes (e.g., "Orleans", "Proto.Actor").
*   **Agent Initialization Parameters:** Allow passing additional structured data or configuration parameters to the root agent beyond the simple prompt string.
*   **Verbosity Control:** Allow users to control log verbosity via a command-line flag (e.g., `-v`, `--verbose`). 