using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.Runtime;
using AgctorSDK.Core.Runtime.Examples;

namespace AgctorSDK.Core
{
    /// <summary>
    /// Simple console application demonstrating the InMemoryActorRuntime functionality.
    /// This showcases actor spawning, message passing, and runtime monitoring.
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Choose demo to run:");
            Console.WriteLine("1. Basic Demo");
            Console.WriteLine("2. Comprehensive Demo");
            Console.WriteLine("3. Performance Test");
            Console.WriteLine("Enter choice (1-3): ");
            
            var choice = Console.ReadLine();
            
            switch (choice)
            {
                case "1":
                    await RunBasicDemo();
                    break;
                case "2":
                    await RuntimeDemo.RunDemoAsync();
                    break;
                case "3":
                    await RuntimeDemo.RunPerformanceTestAsync();
                    break;
                default:
                    Console.WriteLine("Invalid choice, running basic demo...");
                    await RunBasicDemo();
                    break;
            }
        }

        static async Task RunBasicDemo()
        {
            Console.WriteLine("=== Agctor InMemoryActorRuntime Basic Demo ===\n");

            // Create and initialize the runtime
            using var runtime = new InMemoryActorRuntime();
            await runtime.InitializeAsync(new Dictionary<string, object>
            {
                ["MaxConcurrentMessages"] = 100,
                ["Environment"] = "Demo"
            });

            Console.WriteLine("✓ Runtime initialized successfully\n");

            // Spawn some actors
            Console.WriteLine("Spawning actors...");
            var actor1 = await runtime.SpawnActorAsync<EchoActor>("echo-1");
            var actor2 = await runtime.SpawnActorAsync<EchoActor>("echo-2");
            var actor3 = await runtime.SpawnActorAsync<EchoActor>("echo-3");

            Console.WriteLine("✓ Spawned 3 EchoActors\n");

            // Send some messages
            Console.WriteLine("Sending messages...");
            await runtime.SendMessageAsync("echo-1", "Hello from the demo!");
            await runtime.SendMessageAsync("echo-2", "This is a test message");
            await runtime.SendMessageAsync("echo-3", 42); // Test with different data types
            await runtime.SendMessageAsync("echo-1", new { Name = "Demo", Value = 123 });

            // Wait a moment for processing
            await Task.Delay(100);

            // Get runtime statistics
            var stats = await runtime.GetStatisticsAsync();
            Console.WriteLine($"\n📊 Runtime Statistics:");
            Console.WriteLine($"   Active Actors: {stats.ActiveActorCount}");
            Console.WriteLine($"   Messages Processed: {stats.TotalMessagesProcessed}");
            Console.WriteLine($"   Messages/Second: {stats.MessagesPerSecond:F2}");
            Console.WriteLine($"   Avg Processing Time: {stats.AverageMessageProcessingTime:F2}ms");
            Console.WriteLine($"   Uptime: {stats.Uptime.TotalSeconds:F1}s");
            Console.WriteLine($"   Memory Usage: {stats.MemoryUsageBytes / 1024.0 / 1024.0:F2} MB");

            // List active actors
            var activeActors = await runtime.GetActiveActorIdsAsync();
            Console.WriteLine($"\n🎭 Active Actors: {string.Join(", ", activeActors)}");

            // Stop one actor
            Console.WriteLine("\nStopping actor 'echo-2'...");
            await runtime.StopActorAsync("echo-2");

            var remainingActors = await runtime.GetActiveActorIdsAsync();
            Console.WriteLine($"✓ Remaining actors: {string.Join(", ", remainingActors)}");

            Console.WriteLine("\n🎉 Basic demo completed successfully!");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
} 