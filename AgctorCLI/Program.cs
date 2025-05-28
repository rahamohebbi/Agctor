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
    }
}
