using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.DependencyInjection;

namespace AgctorCLI
{
    /// <summary>
    /// Command-line interface for the Agctor SDK demonstrating adapter pattern usage.
    /// Shows how to configure and use different actor runtime backends in a CLI application.
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Agctor CLI - Adapter Pattern Demo ===\n");

            // Parse command line arguments for runtime selection
            var runtimeName = args.Length > 0 ? args[0] : "InMemory";
            
            Console.WriteLine($"🎯 Selected Runtime: {runtimeName}");
            Console.WriteLine("Available runtimes: InMemory, Orleans, Proto.Actor\n");

            // Configure dependency injection with the adapter pattern
            var services = new ServiceCollection();
            
            // Add logging
            services.AddLogging(builder => 
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Register Agctor services with adapter pattern
            services.AddAgctor(options =>
            {
                options.DefaultRuntime = runtimeName;
                options.MaxConcurrentMessages = 1000;
                options.EnableDetailedLogging = true;
                options.Environment = "CLI";
                options.AdditionalProperties["CLIMode"] = true;
            });

            // Build service provider
            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            try
            {
                // Get the adapter factory
                var adapterFactory = serviceProvider.GetRequiredService<IActorRuntimeAdapterFactory>();
                
                logger.LogInformation("📋 Available Runtimes:");
                foreach (var runtime in adapterFactory.GetAvailableRuntimes())
                {
                    var isAvailable = adapterFactory.IsRuntimeAvailable(runtime);
                    var status = isAvailable ? "✅ Available" : "❌ Not Available";
                    logger.LogInformation("   {Runtime}: {Status}", runtime, status);
                }

                // Create the specified runtime
                logger.LogInformation("\n🚀 Creating runtime adapter...");
                var actorRuntime = adapterFactory.CreateRuntime(runtimeName);
                
                logger.LogInformation("✓ Created {RuntimeName} v{Version}", 
                    actorRuntime.Name, actorRuntime.Version);

                // Initialize the runtime
                logger.LogInformation("🔧 Initializing runtime...");
                await actorRuntime.InitializeAsync(new Dictionary<string, object>
                {
                    ["Environment"] = "CLI",
                    ["MaxConcurrentMessages"] = 500,
                    ["EnableMetrics"] = true
                });

                logger.LogInformation("✅ Runtime initialized successfully!");

                // Demonstrate basic operations (only works with InMemory for now)
                if (runtimeName == "InMemory")
                {
                    await DemonstrateActorOperations(actorRuntime, logger);
                    await DemonstrateAgentOperations(actorRuntime, logger);
                }
                else
                {
                    logger.LogWarning("⚠️  {RuntimeName} is a placeholder implementation", runtimeName);
                    logger.LogInformation("   Future versions will include full {RuntimeName} integration", runtimeName);
                }

                // Cleanup
                logger.LogInformation("🧹 Shutting down runtime...");
                await actorRuntime.ShutdownAsync();
                actorRuntime.Dispose();
                
                logger.LogInformation("✅ CLI demo completed successfully!");
            }
            catch (NotImplementedException ex)
            {
                logger.LogWarning("⚠️  Runtime not implemented: {Message}", ex.Message.Split('.')[0]);
                logger.LogInformation("   This is expected for placeholder adapters (Orleans, Proto.Actor)");
                logger.LogInformation("   Use 'InMemory' runtime for a working demonstration");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Error during CLI execution");
            }
            finally
            {
                await serviceProvider.DisposeAsync();
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Demonstrates basic actor operations using the runtime adapter.
        /// This method showcases the adapter pattern in action.
        /// </summary>
        /// <param name="runtime">The actor runtime adapter to use</param>
        /// <param name="logger">Logger for output</param>
        private static async Task DemonstrateActorOperations(IActorRuntimeAdapter runtime, ILogger logger)
        {
            logger.LogInformation("\n🎭 Demonstrating Actor Operations:");

            try
            {
                // Note: For this demo, we'll simulate actor operations since we don't have
                // the EchoActor class available in the CLI project
                
                logger.LogInformation("   📊 Getting runtime statistics...");
                var stats = await runtime.GetStatisticsAsync();
                logger.LogInformation("   ✓ Active Actors: {ActiveActors}", stats.ActiveActorCount);
                logger.LogInformation("   ✓ Messages Processed: {MessagesProcessed}", stats.TotalMessagesProcessed);
                logger.LogInformation("   ✓ Uptime: {Uptime:F1}s", stats.Uptime.TotalSeconds);

                logger.LogInformation("   📋 Getting active actor IDs...");
                var activeActors = await runtime.GetActiveActorIdsAsync();
                logger.LogInformation("   ✓ Found {ActorCount} active actors", activeActors.Count());

                logger.LogInformation("   ✅ Basic operations completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "   ❌ Error during actor operations");
            }
        }

        /// <summary>
        /// Demonstrates agent operations including recursive task decomposition.
        /// This method showcases the agent pattern with prompt processing and subtask assignment.
        /// </summary>
        /// <param name="runtime">The initialized runtime to use</param>
        /// <param name="logger">Logger for output</param>
        private static async Task DemonstrateAgentOperations(IActorRuntimeAdapter runtime, ILogger logger)
        {
            logger.LogInformation("\n🤖 Demonstrating Recursive Agent Operations:");

            try
            {
                // Create an agent factory with the initialized runtime
                var agentFactory = new AgctorSDK.Core.Agents.AgentFactory(runtime);
                
                logger.LogInformation("   📋 Creating root agent...");
                
                // Create a root agent with a complex prompt that will trigger controlled subtask creation
                var rootPrompt = "Please analyze and report on current market trends for comprehensive analysis";
                var rootAgent = await agentFactory.SpawnAgentAsync<AgctorSDK.Core.Agents.Agent>(
                    rootPrompt, 
                    agentId: "root-agent-demo"
                );
                
                logger.LogInformation("   ✓ Root agent created: {AgentId}", rootAgent.Id);
                logger.LogInformation("   ✓ Agent status: {Status}", rootAgent.Status);
                logger.LogInformation("   ✓ Agent hierarchy depth: {Depth}", rootAgent.HierarchyDepth);
                logger.LogInformation("   ✓ Current prompt: {Prompt}", rootAgent.CurrentPrompt);
                
                // Wait for the agent to process and potentially spawn children
                logger.LogInformation("   ⏳ Waiting for agent to process prompt and spawn children...");
                await Task.Delay(1000);
                
                logger.LogInformation("   📊 Checking agent hierarchy after processing...");
                logger.LogInformation("   ✓ Root agent status: {Status}", rootAgent.Status);
                logger.LogInformation("   ✓ Child agents spawned: {ChildCount}", rootAgent.ChildAgentIds.Count);
                
                if (rootAgent.ChildAgentIds.Count > 0)
                {
                    logger.LogInformation("   📋 Child agent details:");
                    foreach (var childId in rootAgent.ChildAgentIds)
                    {
                        var childAgent = await agentFactory.GetAgentAsync(childId);
                        if (childAgent != null)
                        {
                            var childDepth = childAgent is AgctorSDK.Core.Agents.Agent agentImpl ? agentImpl.HierarchyDepth : -1;
                            logger.LogInformation("     - {ChildId} (D{Depth}): {Status} - {Prompt}", 
                                childAgent.Id, childDepth, childAgent.Status, childAgent.CurrentPrompt);
                        }
                    }
                }
                
                // Demonstrate manual subtask assignment with depth control
                logger.LogInformation("   🎯 Testing manual subtask assignment...");
                try
                {
                    var subtaskId = await rootAgent.AssignSubtaskAsync("Validate data sources for the analysis");
                    logger.LogInformation("   ✓ Manual subtask assigned to agent: {SubtaskId}", subtaskId);
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogInformation("   ⚠️  Manual subtask assignment blocked: {Reason}", ex.Message);
                }
                
                // Wait for processing to complete
                logger.LogInformation("   ⏳ Waiting for all processing to complete...");
                await Task.Delay(1500);
                
                // Show final status
                logger.LogInformation("   📈 Final agent hierarchy status:");
                await ShowAgentHierarchy(agentFactory, rootAgent.Id, logger, 0);
                
                // Test depth limits by trying to create a deep hierarchy
                logger.LogInformation("   🔬 Testing hierarchy depth limits...");
                await TestDepthLimits(agentFactory, logger);
                
                // Demonstrate agent factory capabilities
                logger.LogInformation("   🏭 Agent factory capabilities:");
                var registeredTypes = agentFactory.GetRegisteredAgentTypes();
                logger.LogInformation("     Available agent types: {Types}", string.Join(", ", registeredTypes));
                
                // Get runtime statistics
                var stats = await runtime.GetStatisticsAsync();
                logger.LogInformation("   📊 Runtime statistics:");
                logger.LogInformation("     Active actors: {ActiveActors}", stats.ActiveActorCount);
                logger.LogInformation("     Messages processed: {MessagesProcessed}", stats.TotalMessagesProcessed);
                
                // Clean up - stop the root agent (this will cascade to children)
                logger.LogInformation("   🧹 Cleaning up agents...");
                await agentFactory.StopAgentAsync(rootAgent.Id);
                logger.LogInformation("   ✓ Root agent and children stopped");
                
                logger.LogInformation("   ✅ Agent operations completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "   ❌ Error during agent operations");
            }
        }

        /// <summary>
        /// Recursively shows the agent hierarchy for monitoring purposes.
        /// </summary>
        private static async Task ShowAgentHierarchy(AgctorSDK.Core.Agents.AgentFactory agentFactory, string agentId, ILogger logger, int indentLevel)
        {
            var indent = new string(' ', indentLevel * 2);
            var agent = await agentFactory.GetAgentAsync(agentId);
            
            if (agent != null)
            {
                var depth = agent is AgctorSDK.Core.Agents.Agent agentImpl ? agentImpl.HierarchyDepth : -1;
                logger.LogInformation("   {Indent}- {AgentId} (D{Depth}): {Status} - Children: {ChildCount}", 
                    indent, agent.Id, depth, agent.Status, agent.ChildAgentIds.Count);
                
                // Show children recursively
                foreach (var childId in agent.ChildAgentIds)
                {
                    await ShowAgentHierarchy(agentFactory, childId, logger, indentLevel + 1);
                }
            }
        }

        /// <summary>
        /// Tests the hierarchy depth limits to ensure infinite recursion is prevented.
        /// </summary>
        private static async Task TestDepthLimits(AgctorSDK.Core.Agents.AgentFactory agentFactory, ILogger logger)
        {
            try
            {
                // Create a test agent and try to exceed depth limits
                var testAgent = await agentFactory.SpawnAgentAsync<AgctorSDK.Core.Agents.Agent>(
                    "Simple test task", 
                    agentId: "depth-test-agent"
                );
                
                logger.LogInformation("     Testing depth limit enforcement...");
                
                // Try to create a deep hierarchy manually
                var currentAgent = testAgent;
                for (int i = 0; i < 5; i++) // Try to go beyond the limit
                {
                    try
                    {
                        var childId = await currentAgent.AssignSubtaskAsync($"Depth test task level {i + 1}");
                        var childAgent = await agentFactory.GetAgentAsync<AgctorSDK.Core.Agents.Agent>(childId);
                        if (childAgent != null)
                        {
                            currentAgent = childAgent;
                            logger.LogInformation("     ✓ Created agent at depth {Depth}", i + 1);
                        }
                    }
                    catch (InvalidOperationException ex)
                    {
                        logger.LogInformation("     🛑 Depth limit enforced at level {Level}: {Message}", i + 1, ex.Message);
                        break;
                    }
                }
                
                // Clean up test agent
                await agentFactory.StopAgentAsync(testAgent.Id);
                logger.LogInformation("     ✓ Depth limit test completed");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "     ❌ Error during depth limit testing");
            }
        }
    }
}
