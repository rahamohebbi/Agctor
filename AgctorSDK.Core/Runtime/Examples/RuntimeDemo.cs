using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;

namespace AgctorSDK.Core.Runtime.Examples
{
    /// <summary>
    /// Demonstration program showcasing the InMemoryActorRuntime capabilities.
    /// Shows actor spawning, message passing, and runtime monitoring.
    /// </summary>
    public static class RuntimeDemo
    {
        /// <summary>
        /// Runs a comprehensive demo of the InMemoryActorRuntime.
        /// This demonstrates all key features including actor lifecycle, messaging, and monitoring.
        /// </summary>
        public static async Task RunDemoAsync()
        {
            Console.WriteLine("=== Agctor InMemoryActorRuntime Demo ===\n");

            // Create and initialize the runtime
            using var runtime = new InMemoryActorRuntime();
            
            // Subscribe to runtime events for monitoring
            runtime.ActorSpawned += (sender, args) => 
                Console.WriteLine($"🚀 Actor spawned: {args.ActorId} ({args.ActorType}) at {args.Timestamp:HH:mm:ss.fff}");
            
            runtime.ActorStopped += (sender, args) => 
                Console.WriteLine($"🛑 Actor stopped: {args.ActorId} ({args.ActorType}) - {args.Reason} at {args.Timestamp:HH:mm:ss.fff}");
            
            runtime.MessageSent += (sender, args) => 
                Console.WriteLine($"📨 Message sent: {args.MessageId} from {args.SenderId ?? "system"} to {args.ReceiverId} ({args.MessageType}) at {args.Timestamp:HH:mm:ss.fff}");

            try
            {
                // Initialize runtime with configuration
                var config = new Dictionary<string, object>
                {
                    { "MaxActors", 10 },
                    { "LogLevel", "Debug" },
                    { "Environment", "Demo" }
                };

                await runtime.InitializeAsync(config);
                Console.WriteLine("✅ Runtime initialized successfully\n");

                // Demo 1: Basic Actor Spawning and Messaging
                await DemoBasicActorOperations(runtime);

                // Demo 2: Multiple Actors with Concurrent Messaging
                await DemoMultipleActors(runtime);

                // Demo 3: Complex Message Types
                await DemoComplexMessages(runtime);

                // Demo 4: Runtime Statistics
                await DemoRuntimeStatistics(runtime);

                // Demo 5: Actor Lifecycle Management
                await DemoActorLifecycle(runtime);

                Console.WriteLine("\n=== Demo completed successfully! ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Demo failed: {ex.Message}");
                throw;
            }
        }

        private static async Task DemoBasicActorOperations(IActorRuntimeAdapter runtime)
        {
            Console.WriteLine("--- Demo 1: Basic Actor Operations ---");

            // Spawn an echo actor
            var echoActor = await runtime.SpawnActorAsync<EchoActor>("echo-1");
            Console.WriteLine($"✅ Spawned EchoActor: {echoActor.Id} (State: {echoActor.State})");

            // Send simple messages
            await runtime.SendMessageAsync("echo-1", "Hello, World!");
            await runtime.SendMessageAsync("echo-1", 42);
            await runtime.SendMessageAsync("echo-1", "Another message");

            // Wait for processing
            await Task.Delay(100);

            // Retrieve the actor
            var retrievedActor = await runtime.GetActorAsync<EchoActor>("echo-1");
            Console.WriteLine($"✅ Retrieved actor: {retrievedActor?.Id} (Same instance: {ReferenceEquals(echoActor, retrievedActor)})");

            Console.WriteLine();
        }

        private static async Task DemoMultipleActors(IActorRuntimeAdapter runtime)
        {
            Console.WriteLine("--- Demo 2: Multiple Actors with Concurrent Messaging ---");

            // Spawn multiple actors
            var actorIds = new[] { "worker-1", "worker-2", "worker-3" };
            foreach (var actorId in actorIds)
            {
                await runtime.SpawnActorAsync<EchoActor>(actorId);
            }

            Console.WriteLine($"✅ Spawned {actorIds.Length} worker actors");

            // Send messages to all actors concurrently
            var messageTasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                foreach (var actorId in actorIds)
                {
                    messageTasks.Add(runtime.SendMessageAsync(actorId, $"Batch message {i}", "demo-sender"));
                }
            }

            await Task.WhenAll(messageTasks);
            Console.WriteLine($"✅ Sent {messageTasks.Count} messages concurrently");

            // Wait for processing
            await Task.Delay(200);

            // Check active actors
            var activeActors = await runtime.GetActiveActorIdsAsync();
            Console.WriteLine($"✅ Active actors: {string.Join(", ", activeActors)}");

            Console.WriteLine();
        }

        private static async Task DemoComplexMessages(IActorRuntimeAdapter runtime)
        {
            Console.WriteLine("--- Demo 3: Complex Message Types ---");

            // Spawn a specialized actor for complex messages
            await runtime.SpawnActorAsync<EchoActor>("complex-handler");

            // Send complex message types
            var complexRequest = new EchoRequest("Process this complex request", 50, "Important metadata");
            await runtime.SendMessageAsync("complex-handler", complexRequest, "complex-sender");

            // Send message with custom headers
            var headers = new Dictionary<string, string>
            {
                { "Priority", "High" },
                { "Source", "Demo" },
                { "RequestId", Guid.NewGuid().ToString() }
            };

            await runtime.SendMessageAsync("complex-handler", "Message with headers", "header-sender", headers);

            Console.WriteLine("✅ Sent complex messages with metadata and headers");

            // Wait for processing
            await Task.Delay(150);

            Console.WriteLine();
        }

        private static async Task DemoRuntimeStatistics(IActorRuntimeAdapter runtime)
        {
            Console.WriteLine("--- Demo 4: Runtime Statistics ---");

            var stats = await runtime.GetStatisticsAsync();

            Console.WriteLine($"📊 Runtime Statistics:");
            Console.WriteLine($"   • Active Actors: {stats.ActiveActorCount}");
            Console.WriteLine($"   • Total Messages Processed: {stats.TotalMessagesProcessed}");
            Console.WriteLine($"   • Messages/Second: {stats.MessagesPerSecond:F2}");
            Console.WriteLine($"   • Avg Processing Time: {stats.AverageMessageProcessingTime:F2}ms");
            Console.WriteLine($"   • Uptime: {stats.Uptime:hh\\:mm\\:ss}");
            Console.WriteLine($"   • Memory Usage: {stats.MemoryUsageBytes:N0} bytes");

            if (stats.AdditionalMetrics.Count > 0)
            {
                Console.WriteLine($"   • Additional Metrics:");
                foreach (var metric in stats.AdditionalMetrics)
                {
                    Console.WriteLine($"     - {metric.Key}: {metric.Value}");
                }
            }

            Console.WriteLine();
        }

        private static async Task DemoActorLifecycle(IActorRuntimeAdapter runtime)
        {
            Console.WriteLine("--- Demo 5: Actor Lifecycle Management ---");

            // Spawn a temporary actor
            var tempActor = await runtime.SpawnActorAsync<EchoActor>("temp-actor");
            Console.WriteLine($"✅ Spawned temporary actor: {tempActor.Id}");

            // Send a few messages
            await runtime.SendMessageAsync("temp-actor", "Message 1");
            await runtime.SendMessageAsync("temp-actor", "Message 2");

            // Wait for processing
            await Task.Delay(50);

            // Stop the actor
            await runtime.StopActorAsync("temp-actor");
            Console.WriteLine($"✅ Stopped actor: temp-actor (Final state: {tempActor.State})");

            // Verify actor is no longer active
            var stoppedActor = await runtime.GetActorAsync<EchoActor>("temp-actor");
            Console.WriteLine($"✅ Actor retrieval after stop: {(stoppedActor == null ? "null (expected)" : "still exists")}");

            // Show final statistics
            var finalStats = await runtime.GetStatisticsAsync();
            Console.WriteLine($"✅ Final active actor count: {finalStats.ActiveActorCount}");

            Console.WriteLine();
        }

        /// <summary>
        /// Runs a simple performance test to demonstrate runtime capabilities.
        /// </summary>
        public static async Task RunPerformanceTestAsync()
        {
            Console.WriteLine("=== Performance Test ===\n");

            using var runtime = new InMemoryActorRuntime();
            await runtime.InitializeAsync(new Dictionary<string, object>());

            const int actorCount = 10;
            const int messagesPerActor = 100;

            Console.WriteLine($"Creating {actorCount} actors and sending {messagesPerActor} messages each...");

            var startTime = DateTimeOffset.UtcNow;

            // Spawn actors
            var spawnTasks = new List<Task>();
            for (int i = 0; i < actorCount; i++)
            {
                spawnTasks.Add(runtime.SpawnActorAsync<EchoActor>($"perf-actor-{i}"));
            }
            await Task.WhenAll(spawnTasks);

            var spawnTime = DateTimeOffset.UtcNow - startTime;
            Console.WriteLine($"✅ Spawned {actorCount} actors in {spawnTime.TotalMilliseconds:F2}ms");

            // Send messages
            var messageStartTime = DateTimeOffset.UtcNow;
            var messageTasks = new List<Task>();

            for (int i = 0; i < actorCount; i++)
            {
                var actorId = $"perf-actor-{i}";
                for (int j = 0; j < messagesPerActor; j++)
                {
                    messageTasks.Add(runtime.SendMessageAsync(actorId, $"Message {j}"));
                }
            }

            await Task.WhenAll(messageTasks);
            var sendTime = DateTimeOffset.UtcNow - messageStartTime;

            Console.WriteLine($"✅ Sent {actorCount * messagesPerActor} messages in {sendTime.TotalMilliseconds:F2}ms");
            Console.WriteLine($"   • Send rate: {(actorCount * messagesPerActor) / sendTime.TotalSeconds:F0} messages/second");

            // Wait for processing
            await Task.Delay(1000);

            // Get final statistics
            var stats = await runtime.GetStatisticsAsync();
            var totalTime = DateTimeOffset.UtcNow - startTime;

            Console.WriteLine($"\n📊 Performance Results:");
            Console.WriteLine($"   • Total time: {totalTime.TotalMilliseconds:F2}ms");
            Console.WriteLine($"   • Messages processed: {stats.TotalMessagesProcessed}");
            Console.WriteLine($"   • Processing rate: {stats.MessagesPerSecond:F2} messages/second");
            Console.WriteLine($"   • Memory usage: {stats.MemoryUsageBytes:N0} bytes");

            Console.WriteLine("\n=== Performance test completed ===");
        }
    }
} 