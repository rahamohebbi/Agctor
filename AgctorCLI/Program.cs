using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Agents;

namespace AgctorCLI
{
    /// <summary>
    /// CLI Agent Runner - A simple command-line interface for processing prompts through the Agctor agent system.
    /// Accepts prompts via command line arguments, dispatches them to a root agent, and prints results to console.
    /// </summary>
    class Program
    {
        /// <summary>
        /// Main entry point for the CLI Agent Runner.
        /// Usage: AgctorCLI.exe "Your prompt here" [runtime]
        /// </summary>
        /// <param name="args">Command line arguments: [0] = prompt, [1] = optional runtime name</param>
        static async Task Main(string[] args)
        {
            // Validate command line arguments - prompt is required
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                ShowUsage();
                return;
            }

            var prompt = args[0];
            var runtimeName = args.Length > 1 ? args[1] : "InMemory"; // Default to InMemory runtime

            Console.WriteLine("🤖 Agctor CLI Agent Runner");
            Console.WriteLine($"📝 Prompt: {prompt}");
            Console.WriteLine($"⚙️  Runtime: {runtimeName}");
            Console.WriteLine();

            try
            {
                // Configure dependency injection container with Agctor services
                var serviceProvider = ConfigureDependencyInjection(runtimeName);
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

                // Initialize the actor runtime for agent operations
                var runtime = await InitializeRuntimeAsync(serviceProvider, runtimeName, logger);
                
                // Process the prompt through the root agent and get result
                var result = await ProcessPromptWithRootAgent(runtime, prompt, logger);
                
                // Print the final result to console
                Console.WriteLine("✅ Result:");
                Console.WriteLine(result);

                // Clean up resources
                await runtime.ShutdownAsync();
                runtime.Dispose();
                await serviceProvider.DisposeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }

        /// <summary>
        /// Configures the dependency injection container with all required services.
        /// Sets up logging, Agctor services, and runtime adapters.
        /// </summary>
        /// <param name="runtimeName">The name of the runtime to configure</param>
        /// <returns>Configured service provider with all dependencies</returns>
        private static ServiceProvider ConfigureDependencyInjection(string runtimeName)
        {
            var services = new ServiceCollection();
            
            // Add console logging for debugging and monitoring
            services.AddLogging(builder => 
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Register Agctor services with the specified runtime
            services.AddAgctor(options =>
            {
                options.DefaultRuntime = runtimeName;
                options.MaxConcurrentMessages = 100;
                options.EnableDetailedLogging = false; // Keep it simple for CLI
                options.Environment = "CLI";
            });

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Initializes the actor runtime adapter for processing agent operations.
        /// Creates and configures the runtime with appropriate settings for CLI usage.
        /// </summary>
        /// <param name="serviceProvider">DI container with configured services</param>
        /// <param name="runtimeName">Name of the runtime to initialize</param>
        /// <param name="logger">Logger for runtime initialization messages</param>
        /// <returns>Initialized and ready-to-use runtime adapter</returns>
        private static async Task<IActorRuntimeAdapter> InitializeRuntimeAsync(
            ServiceProvider serviceProvider, 
            string runtimeName, 
            ILogger logger)
        {
            logger.LogInformation("🚀 Initializing {RuntimeName} runtime...", runtimeName);
            
            var adapterFactory = serviceProvider.GetRequiredService<IActorRuntimeAdapterFactory>();
            
            // Verify the runtime is available before attempting to create it
            if (!adapterFactory.IsRuntimeAvailable(runtimeName))
            {
                throw new InvalidOperationException($"Runtime '{runtimeName}' is not available. Available runtimes: {string.Join(", ", adapterFactory.GetAvailableRuntimes())}");
            }

            var runtime = adapterFactory.CreateRuntime(runtimeName);
            
            // Initialize with CLI-appropriate configuration
            await runtime.InitializeAsync(new Dictionary<string, object>
            {
                ["Environment"] = "CLI",
                ["MaxConcurrentMessages"] = 50,
                ["EnableMetrics"] = false // Keep overhead low for CLI
            });

            logger.LogInformation("✅ Runtime initialized successfully");
            return runtime;
        }

        /// <summary>
        /// Processes the user prompt through a root agent and returns the result.
        /// Creates a root agent, assigns the prompt, waits for completion, and retrieves the result.
        /// </summary>
        /// <param name="runtime">Initialized runtime adapter</param>
        /// <param name="prompt">User prompt to process</param>
        /// <param name="logger">Logger for operation tracking</param>
        /// <returns>The result from processing the prompt</returns>
        private static async Task<string> ProcessPromptWithRootAgent(
            IActorRuntimeAdapter runtime, 
            string prompt, 
            ILogger logger)
        {
            logger.LogInformation("🤖 Creating root agent for prompt processing...");
            
            // Create agent factory for spawning and managing agents
            var agentFactory = new AgentFactory(runtime);
            
            // Spawn a root agent with the user's prompt
            var rootAgent = await agentFactory.SpawnAgentAsync<Agent>(
                prompt, 
                agentId: $"cli-root-{Guid.NewGuid():N}"
            );

            logger.LogInformation("✅ Root agent created: {AgentId}", rootAgent.Id);
            logger.LogInformation("⏳ Processing prompt...");

            // Wait for the agent to complete processing the prompt
            // In a real implementation, you might want to add timeout and progress monitoring
            var maxWaitTime = TimeSpan.FromMinutes(5); // Reasonable timeout for CLI operations
            var startTime = DateTime.UtcNow;
            
            while (rootAgent.Status != AgentStatus.Completed && 
                   rootAgent.Status != AgentStatus.Failed &&
                   DateTime.UtcNow - startTime < maxWaitTime)
            {
                await Task.Delay(500); // Poll every 500ms
                
                // Log progress for long-running operations
                if ((DateTime.UtcNow - startTime).TotalSeconds % 10 == 0)
                {
                    logger.LogInformation("⏳ Still processing... Status: {Status}, Children: {ChildCount}", 
                        rootAgent.Status, rootAgent.ChildAgentIds.Count);
                }
            }

            // Check final status and return appropriate result
            if (rootAgent.Status == AgentStatus.Completed)
            {
                logger.LogInformation("✅ Prompt processing completed successfully");
                
                // For this simple CLI, we'll return a basic completion message
                // In a more sophisticated implementation, you'd extract the actual result from the agent
                return $"Prompt processed successfully by agent {rootAgent.Id}. " +
                       $"Agent spawned {rootAgent.ChildAgentIds.Count} child agents for subtask processing.";
            }
            else if (rootAgent.Status == AgentStatus.Failed)
            {
                logger.LogError("❌ Prompt processing failed");
                return "Prompt processing failed. Please check the logs for more details.";
            }
            else
            {
                logger.LogWarning("⏰ Prompt processing timed out after {Timeout} minutes", maxWaitTime.TotalMinutes);
                return $"Prompt processing timed out. Agent status: {rootAgent.Status}";
            }
        }

        /// <summary>
        /// Displays usage information for the CLI Agent Runner.
        /// Shows the correct command format and available options.
        /// </summary>
        private static void ShowUsage()
        {
            Console.WriteLine("🤖 Agctor CLI Agent Runner");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  AgctorCLI.exe \"Your prompt here\" [runtime]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  prompt   - The prompt or task to process (required, use quotes for multi-word prompts)");
            Console.WriteLine("  runtime  - The runtime to use (optional, defaults to 'InMemory')");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  AgctorCLI.exe \"Analyze the current market trends\"");
            Console.WriteLine("  AgctorCLI.exe \"Generate a report on sales data\" InMemory");
            Console.WriteLine();
            Console.WriteLine("Available runtimes: InMemory, Orleans, Proto.Actor");
            Console.WriteLine("Note: Only InMemory runtime is fully implemented in this version.");
        }
    }
}
