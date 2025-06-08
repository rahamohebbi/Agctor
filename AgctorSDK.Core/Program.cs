using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Runtime.Examples;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.ErrorHandling;

namespace AgctorSDK.Core
{
    /// <summary>
    /// Console application demonstrating the Agctor adapter pattern system.
    /// Shows how to configure and use different actor runtime backends through dependency injection.
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            // Initialize the logging system
            Utils.Logging.LoggerFactory.SetDefaultMinLevel(Utils.Logging.LogLevel.Debug);
            var logger = Utils.Logging.LoggerFactory.CreateLogger("Program");
            
            logger.Info("=== Agctor Adapter Pattern Demo ===\n");
            
            Console.WriteLine("Choose demo to run:");
            Console.WriteLine("1. Basic Demo (InMemory Runtime)");
            Console.WriteLine("2. Adapter Pattern Demo");
            Console.WriteLine("3. Dependency Injection Demo");
            Console.WriteLine("4. Runtime Switching Demo");
            Console.WriteLine("5. Comprehensive Demo");
            Console.WriteLine("6. Performance Test");
            Console.Write("Enter choice (1-6): ");
            
            var choice = Console.ReadLine();
            
            switch (choice)
            {
                case "1":
                    await RunBasicDemo();
                    break;
                case "2":
                    await RunAdapterPatternDemo();
                    break;
                case "3":
                    await RunDependencyInjectionDemo();
                    break;
                case "4":
                    await RunRuntimeSwitchingDemo();
                    break;
                case "5":
                    await RuntimeDemo.RunDemoAsync();
                    break;
                case "6":
                    await RuntimeDemo.RunPerformanceTestAsync();
                    break;
                default:
                    Console.WriteLine("Invalid choice, running adapter pattern demo...");
                    await RunAdapterPatternDemo();
                    break;
            }
        }

        /// <summary>
        /// Demonstrates basic usage with the InMemoryActorRuntime (legacy approach).
        /// </summary>
        static async Task RunBasicDemo()
        {
            Console.WriteLine("=== Basic Demo (Direct InMemory Runtime) ===\n");

            // Create and initialize the runtime directly (legacy approach)
            using var runtime = new InMemoryActorRuntime();
            await runtime.InitializeAsync(new Dictionary<string, object>
            {
                ["MaxConcurrentMessages"] = 100,
                ["Environment"] = "Demo"
            });

            Console.WriteLine("✓ InMemory runtime initialized directly\n");

            // Spawn and test actors
            await DemoActorOperations(runtime);
        }

        /// <summary>
        /// Demonstrates the adapter pattern with different runtime backends.
        /// </summary>
        static async Task RunAdapterPatternDemo()
        {
            Console.WriteLine("=== Adapter Pattern Demo ===\n");

            // Configure services with dependency injection
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information));
            
            // Register Agctor services with InMemory as default
            services.AddAgctor(options =>
            {
                options.DefaultRuntime = "InMemory";
                options.MaxConcurrentMessages = 500;
                options.EnableDetailedLogging = true;
                options.Environment = "AdapterDemo";
            });

            var serviceProvider = services.BuildServiceProvider();

            // Get the adapter factory
            var adapterFactory = serviceProvider.GetRequiredService<IActorRuntimeAdapterFactory>();

            Console.WriteLine("📋 Available Runtimes:");
            foreach (var runtimeName in adapterFactory.GetAvailableRuntimes())
            {
                var isAvailable = adapterFactory.IsRuntimeAvailable(runtimeName);
                var status = isAvailable ? "✅ Available" : "❌ Not Available";
                Console.WriteLine($"   {runtimeName}: {status}");
            }

            Console.WriteLine($"\n🎯 Default Runtime: {adapterFactory.GetDefaultRuntimeName()}\n");

            // Test InMemory runtime (should work)
            await TestRuntimeAdapter(adapterFactory, "InMemory");

            // Test Orleans runtime (should throw NotImplementedException)
            await TestRuntimeAdapter(adapterFactory, "Orleans");

            // Test Proto.Actor runtime (should throw NotImplementedException)
            await TestRuntimeAdapter(adapterFactory, "Proto.Actor");

            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// Demonstrates dependency injection configuration with different runtime options.
        /// </summary>
        static async Task RunDependencyInjectionDemo()
        {
            Console.WriteLine("=== Dependency Injection Demo ===\n");

            // Test different DI registration methods
            await TestDIRegistration("AddAgctor() - Default InMemory", services => services.AddAgctor());
            await TestDIRegistration("AddAgctorInMemory() - Explicit InMemory", services => services.AddAgctorInMemory());
            
            // These will demonstrate the placeholder adapters
            Console.WriteLine("⚠️  Testing placeholder adapters (will show NotImplementedException):\n");
            await TestDIRegistration("AddAgctorOrleans() - Orleans Placeholder", services => services.AddAgctorOrleans(), expectException: true);
            await TestDIRegistration("AddAgctorProtoActor() - Proto.Actor Placeholder", services => services.AddAgctorProtoActor(), expectException: true);
        }

        /// <summary>
        /// Demonstrates runtime switching capabilities using the adapter factory.
        /// </summary>
        static async Task RunRuntimeSwitchingDemo()
        {
            Console.WriteLine("=== Runtime Switching Demo ===\n");

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
            services.AddAgctor();

            var serviceProvider = services.BuildServiceProvider();
            var factory = serviceProvider.GetRequiredService<IActorRuntimeAdapterFactory>();

            Console.WriteLine("🔄 Demonstrating runtime switching...\n");

            // Create different runtime instances
            var inMemoryRuntime = factory.CreateRuntime("InMemory");
            Console.WriteLine($"✓ Created {inMemoryRuntime.Name} v{inMemoryRuntime.Version}");

            try
            {
                var orleansRuntime = factory.CreateRuntime("Orleans");
                Console.WriteLine($"✓ Created {orleansRuntime.Name} v{orleansRuntime.Version}");
            }
            catch (NotImplementedException ex)
            {
                Console.WriteLine($"⚠️  Orleans runtime: {ex.Message.Split('.')[0]}");
            }

            try
            {
                var protoActorRuntime = factory.CreateRuntime("Proto.Actor");
                Console.WriteLine($"✓ Created {protoActorRuntime.Name} v{protoActorRuntime.Version}");
            }
            catch (NotImplementedException ex)
            {
                Console.WriteLine($"⚠️  Proto.Actor runtime: {ex.Message.Split('.')[0]}");
            }

            // Test the working runtime
            await inMemoryRuntime.InitializeAsync(new Dictionary<string, object>
            {
                ["Environment"] = "SwitchingDemo"
            });

            Console.WriteLine($"\n🎭 Testing {inMemoryRuntime.Name} runtime:");
            await DemoActorOperations(inMemoryRuntime);

            inMemoryRuntime.Dispose();
            await serviceProvider.DisposeAsync();
        }

        /// <summary>
        /// Tests a specific runtime adapter and handles exceptions gracefully.
        /// </summary>
        static async Task TestRuntimeAdapter(IActorRuntimeAdapterFactory factory, string runtimeName)
        {
            Console.WriteLine($"🧪 Testing {runtimeName} Runtime:");
            
            try
            {
                var runtime = factory.CreateRuntime(runtimeName);
                Console.WriteLine($"   ✓ Created {runtime.Name} v{runtime.Version}");
                
                // Try to initialize the runtime
                await runtime.InitializeAsync(new Dictionary<string, object>
                {
                    ["Environment"] = "Test",
                    ["MaxConcurrentMessages"] = 100
                });
                
                Console.WriteLine($"   ✓ {runtime.Name} initialized successfully");
                
                // Test basic operations if initialization succeeded
                await DemoActorOperations(runtime);
                
                runtime.Dispose();
            }
            catch (NotImplementedException ex)
            {
                Console.WriteLine($"   ⚠️  {runtimeName} is a placeholder: {ex.Message.Split('.')[0]}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error testing {runtimeName}: {ex.Message}");
            }
            
            Console.WriteLine();
        }

        /// <summary>
        /// Tests different dependency injection registration methods.
        /// </summary>
        static async Task TestDIRegistration(string testName, Action<IServiceCollection> configureServices, bool expectException = false)
        {
            Console.WriteLine($"🧪 {testName}:");
            
            try
            {
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
                configureServices(services);

                var serviceProvider = services.BuildServiceProvider();
                var runtime = serviceProvider.GetRequiredService<IActorRuntimeAdapter>();
                
                Console.WriteLine($"   ✓ Resolved {runtime.Name} v{runtime.Version}");
                
                if (!expectException)
                {
                    await runtime.InitializeAsync(new Dictionary<string, object>
                    {
                        ["Environment"] = "DITest"
                    });
                    Console.WriteLine($"   ✓ {runtime.Name} initialized successfully");
                }
                else
                {
                    // Try initialization to trigger NotImplementedException
                    await runtime.InitializeAsync(new Dictionary<string, object>());
                }
                
                runtime.Dispose();
                await serviceProvider.DisposeAsync();
            }
            catch (NotImplementedException ex)
            {
                if (expectException)
                {
                    Console.WriteLine($"   ⚠️  Expected placeholder behavior: {ex.Message.Split('.')[0]}");
                }
                else
                {
                    Console.WriteLine($"   ❌ Unexpected NotImplementedException: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error: {ex.Message}");
            }
            
            Console.WriteLine();
        }

        /// <summary>
        /// Demonstrates basic actor operations with any runtime adapter.
        /// </summary>
        static async Task DemoActorOperations(IActorRuntimeAdapter runtime)
        {
            try
            {
                // Spawn some actors
                Console.WriteLine("   Spawning actors...");
                var actor1 = await runtime.SpawnActorAsync<EchoActor>("echo-1");
                var actor2 = await runtime.SpawnActorAsync<EchoActor>("echo-2");
                Console.WriteLine("   ✓ Spawned 2 EchoActors");

                // Send some messages
                Console.WriteLine("   Sending messages...");
                await runtime.SendMessageAsync("echo-1", "Hello from adapter demo!");
                await runtime.SendMessageAsync("echo-2", "Testing adapter pattern");
                Console.WriteLine("   ✓ Messages sent");

                // Wait a moment for processing
                await Task.Delay(100);

                // Get statistics
                var stats = await runtime.GetStatisticsAsync();
                Console.WriteLine($"   📊 Stats: {stats.ActiveActorCount} actors, {stats.TotalMessagesProcessed} messages processed");

                // Clean up
                await runtime.StopActorAsync("echo-1");
                await runtime.StopActorAsync("echo-2");
                Console.WriteLine("   ✓ Actors stopped");
            }
            catch (NotImplementedException ex)
            {
                Console.WriteLine($"   ⚠️  Operation not implemented: {ex.Message.Split('.')[0]}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Error during operations: {ex.Message}");
            }
        }
    }
} 