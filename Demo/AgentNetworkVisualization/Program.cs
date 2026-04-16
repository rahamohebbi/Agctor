using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.Observability.Visualization;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.ActivityTracking.OpenTelemetry;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Registry;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Agctor.Demo.AgentNetworkVisualization
{
    class Program
    {
        // Configuration for our demo
        private const int MinAgents = 25;
        private const int MaxAgents = 50;
        private const int MinToolsPerAgent = 1;
        private const int MaxToolsPerAgent = 5;
        private const int MaxChildrenPerAgent = 5;
        private const int MaxDepth = 4;
        private const string ZipkinBaseUrl = "http://localhost:9411";
        private const string ZipkinApiEndpoint = "http://localhost:9411/api/v2/spans";
        private const int ConnectivityTimeoutMs = 3000; // 3 seconds timeout
        private const int GlobalExecutionTimeoutMs = 20000; // 20 seconds max execution time
        
        // Lists to track our generated entities
        private static readonly List<MockAgent> Agents = new();
        private static readonly List<MockTool> Tools = new();
        private static readonly Random Random = new(42); // Fixed seed for reproducibility
        private static TracerProvider? CurrentTracerProvider;
        
        // Agent types for variety
        private static readonly string[] AgentTypes = {
            "Analyst", "Researcher", "Writer", "Developer", "Designer", 
            "Coordinator", "Planner", "Validator", "Tester", "Executor"
        };
        
        // Tool types for variety
        private static readonly string[] ToolTypes = {
            "SearchTool", "CalculationTool", "DatabaseTool", "APITool", "FileTool",
            "ValidationTool", "VisualizationTool", "TranslationTool", "AnalysisTool", "GenerationTool"
        };
        
        // Source name for Zipkin - use the same name that works in the example
        private const string SourceName = "AgctorDemo";
        
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Agctor Agent Network Visualization Demo ===");
            Console.WriteLine("This demo will generate 25-50 agents and tools, and visualize their relationships and interactions.");
            
            // Parse command-line arguments
            bool skipTracing = args.Contains("--no-trace");
            
            if (skipTracing)
            {
                Console.WriteLine("Running in no-trace mode (skipping Zipkin connectivity)");
                await GenerateSimpleVisualizationAsync();
                return;
            }
            
            Console.WriteLine($"The demo will attempt to send trace data to Zipkin at: {ZipkinBaseUrl}");
            Console.WriteLine($"Zipkin UI should be accessible at: {ZipkinBaseUrl}/zipkin/");
            Console.WriteLine($"The demo will automatically time out after {GlobalExecutionTimeoutMs/1000} seconds if it hangs.");
            Console.WriteLine("To skip tracing, run with: dotnet run --project AgentNetworkVisualization -- --no-trace");
            
            // Create a cancellation token with timeout
            using var cts = new CancellationTokenSource(GlobalExecutionTimeoutMs);
            
            try
            {
                await RunDemoAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"\n⚠️ Demo execution timed out after {GlobalExecutionTimeoutMs/1000} seconds.");
                Console.WriteLine("Generating visualization without distributed tracing...");
                
                // If we timeout, generate a simple visualization without tracing
                try
                {
                    await GenerateSimpleVisualizationAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n❌ Error generating visualization: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
            }
        }
        
        static async Task GenerateSimpleVisualizationAsync()
        {
            // Create a simple mock network
            var rootAgent = new MockAgent("root-agent", "RootCoordinator", "Main coordinator agent", null);
            Agents.Add(rootAgent);
            
            for (int i = 1; i <= 10; i++)
            {
                string agentType = AgentTypes[Random.Next(AgentTypes.Length)];
                string id = $"agent-{i}";
                string description = $"{agentType} agent";
                
                var agent = new MockAgent(id, agentType, description, rootAgent.Id);
                Agents.Add(agent);
                rootAgent.AddChild(agent.Id);
                
                // Add some tools
                GenerateToolsForAgent(agent);
            }
            
            // Generate visualizations
            string hierarchyDiagram = GenerateAgentHierarchyDiagram();
            Console.WriteLine("\nAgent Hierarchy Diagram generated");
            
            string messageFlowDiagram = GenerateMessageFlowDiagram();
            Console.WriteLine("Message Flow Diagram generated");
            
            // Generate HTML without Zipkin integration
            string html = GenerateVisualizationHtml(hierarchyDiagram, messageFlowDiagram, "no-trace-id", false);
            string outputFolder = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".";
            string outputFile = Path.Combine(outputFolder, "agent_network_visualization.html");
            File.WriteAllText(outputFile, html);
            
            Console.WriteLine($"\nVisualization HTML saved to: {outputFile}");
            Console.WriteLine("Open this file in a web browser to see the visualizations");
            
            Console.WriteLine($"\nCreated {Agents.Count} agents and {Tools.Count} tools");
            Console.WriteLine($"Root agent ID: {rootAgent.Id}");
            Console.WriteLine("\nDemo completed successfully (without tracing). 🎉");
        }
        
        static async Task RunDemoAsync(CancellationToken cancellationToken)
        {
            // Check Zipkin connectivity before proceeding
            bool zipkinAvailable = await CheckZipkinConnectivityAsync();
            bool proceedWithoutTracing = false;
            
            if (!zipkinAvailable)
            {
                Console.WriteLine("\nZipkin connectivity check failed. You have the following options:");
                Console.WriteLine("1. Ensure Zipkin is running and try again");
                Console.WriteLine("2. Continue without distributed tracing");
                Console.WriteLine("3. Exit");
                
                // Automatically continue without tracing to avoid hanging
                Console.WriteLine("\nAutomatically selecting option 2: Continue without distributed tracing");
                proceedWithoutTracing = true;
            }
            else
            {
                Console.WriteLine("✅ Zipkin connectivity check passed.");
            }
            
            // Setup dependency injection
            var services = new ServiceCollection();
            
            // Add logger
            var logger = LoggerFactory.CreateLogger("AgentNetworkVisualization");
            services.AddSingleton<IAgctorLogger>(logger);
            
            // Add basic Agctor services
            services.AddAgctor(options =>
            {
                options.DefaultRuntime = "InMemory";
                options.MaxConcurrentMessages = 100;
                options.EnableDetailedLogging = true;
                options.Environment = "Visualization";
            });
            
            // Configure OpenTelemetry with Zipkin if available
            if (zipkinAvailable && !proceedWithoutTracing)
            {
                // Create a more stable TracerProvider configuration - use exact same approach as VisualizationExample
                var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService(SourceName, serviceInstanceId: Guid.NewGuid().ToString()))
                    .AddSource(SourceName)
                    .AddConsoleExporter(); // For debugging
                
                // Try Jaeger approach
                tracerProviderBuilder.AddJaegerExporter(opts =>
                {
                    // Primary approach: use UDP agent
                    opts.AgentHost = "localhost";
                    opts.AgentPort = 6831;
                    
                    // Also try HTTP collector as backup
                    opts.Endpoint = new Uri("http://localhost:14268/api/traces");
                    
                    // Configure for more reliable exports
                    opts.MaxPayloadSizeInBytes = 8192;  // Increased from 4096
                    
                    // Use Batch processor for more reliable exports
                    opts.ExportProcessorType = ExportProcessorType.Batch;
                    opts.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                    {
                        MaxQueueSize = 4096,               // Increased from 2048
                        ScheduledDelayMilliseconds = 1000, // Decreased from 5000 to flush more frequently
                        ExporterTimeoutMilliseconds = 60000, // Increased from 30000
                        MaxExportBatchSize = 1024          // Increased from 512
                    };
                    
                    Console.WriteLine("Configured Jaeger exporter with both UDP and HTTP endpoints");
                });
                
                // Also try Zipkin as an alternative
                tracerProviderBuilder.AddZipkinExporter(opts =>
                {
                    opts.Endpoint = new Uri(ZipkinApiEndpoint);
                    opts.ExportProcessorType = ExportProcessorType.Batch;
                    opts.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                    {
                        MaxQueueSize = 4096,
                        ScheduledDelayMilliseconds = 1000,
                        ExporterTimeoutMilliseconds = 60000,
                        MaxExportBatchSize = 1024
                    };
                    
                    Console.WriteLine("Configured Zipkin exporter with endpoint: " + ZipkinApiEndpoint);
                    logger.Info($"Configured OpenTelemetry with Zipkin endpoint: {ZipkinApiEndpoint}");
                });
                
                // Build and register the tracer provider - store in class-level field for later flushing
                CurrentTracerProvider = tracerProviderBuilder.Build();
                services.AddSingleton<TracerProvider>(CurrentTracerProvider);
                
                // Create and register the ActivitySource that will generate trace data
                var activitySource = new ActivitySource(SourceName);
                services.AddSingleton(activitySource);
                
                // Create and register our custom activity tracker that uses the ActivitySource
                services.AddSingleton<IActivityTracker>(sp => 
                    new ActivitySourceTracker(activitySource, logger));
                
                logger.Info($"Using direct OpenTelemetry tracing with source/service name: {SourceName}");
            }
            else
            {
                // Use a logger-based activity tracker that doesn't export to Zipkin
                services.AddSingleton<IActivityTracker, AgctorSDK.Core.Utils.ActivityTracking.Logger.LoggerActivityTracker>();
                logger.Info("Using logger-based activity tracking (no distributed tracing)");
                proceedWithoutTracing = true;
            }
            
            // Register visualization options
            var visualizationOptions = new VisualizationOptions
            {
                TraceViewerType = zipkinAvailable && !proceedWithoutTracing ? TraceViewerType.Zipkin : TraceViewerType.None,
                ZipkinBaseUrl = ZipkinBaseUrl
            };
            services.AddSingleton(visualizationOptions);
            
            // Register agent registry
            services.AddSingleton<IAgentRegistry, InMemoryAgentRegistry>();
            
            // Register visualization service
            services.AddSingleton<IVisualizationService, VisualizationService>(sp => new VisualizationService(
                sp.GetRequiredService<IAgentRegistry>(),
                sp.GetRequiredService<IActivityTracker>(),
                sp.GetRequiredService<IAgctorLogger>(),
                visualizationOptions
            ));
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the required services
            var visualizationService = serviceProvider.GetRequiredService<IVisualizationService>();
            var activityTracker = serviceProvider.GetRequiredService<IActivityTracker>();
            var agentRegistry = serviceProvider.GetRequiredService<IAgentRegistry>();
            
            logger.Info("Starting Agent Network Visualization Demo");
            
            try
            {
                // Generate agent network
                await GenerateAgentNetworkAsync(agentRegistry, cancellationToken);
                
                // Create a real trace with activities
                string traceId = await GenerateAgentActivityTraceAsync(activityTracker, logger, cancellationToken);
                
                // Allow time for the trace to be exported to Zipkin
                if (zipkinAvailable && !proceedWithoutTracing)
                {
                    logger.Info("Waiting for trace data to be exported to Zipkin...");
                    await Task.Delay(1000, cancellationToken); // Shorter delay to prevent hanging
                }
                
                // Generate visualizations
                string hierarchyDiagram = GenerateAgentHierarchyDiagram();
                Console.WriteLine("\nAgent Hierarchy Diagram generated");
                
                string messageFlowDiagram = GenerateMessageFlowDiagram();
                Console.WriteLine("Message Flow Diagram generated");
                
                // Generate HTML with the visualizations
                string html = GenerateVisualizationHtml(hierarchyDiagram, messageFlowDiagram, traceId, zipkinAvailable && !proceedWithoutTracing);
                string outputFolder = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? ".";
                string outputFile = Path.Combine(outputFolder, "agent_network_visualization.html");
                File.WriteAllText(outputFile, html);
                
                logger.Info($"Visualization HTML saved to: {outputFile}");
                logger.Info("Open this file in a web browser to see the visualizations");
                
                // If Zipkin is running, show how to access the trace
                if (zipkinAvailable && !proceedWithoutTracing)
                {
                    string zipkinUrl = $"{ZipkinBaseUrl}/zipkin/traces/{traceId}?serviceName={SourceName}";
                    logger.Info($"You can view traces at: {zipkinUrl}");
                }
                
                // Summary of what we created
                Console.WriteLine($"\nCreated {Agents.Count} agents and {Tools.Count} tools");
                Console.WriteLine($"Root agent ID: {Agents.First().Id}");
                Console.WriteLine("\nDemo completed successfully. 🎉");
                
                // Cleanup OpenTelemetry resources
                if (CurrentTracerProvider != null)
                {
                    Console.WriteLine("Shutting down OpenTelemetry TracerProvider...");
                    CurrentTracerProvider.Shutdown();
                    CurrentTracerProvider = null;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An error occurred during demo execution");
                Console.WriteLine($"\nError: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                Console.WriteLine("\nStack trace:");
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }
        
        private static async Task<bool> CheckZipkinConnectivityAsync()
        {
            Console.WriteLine($"Checking if Zipkin is already running on port 9411...");
            
            try
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMilliseconds(ConnectivityTimeoutMs);
                
                // Check Zipkin endpoint
                var response = await httpClient.GetAsync($"{ZipkinBaseUrl}/api/v2/services");
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Zipkin API is accessible. Status: {response.StatusCode}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Zipkin API returned status code: {response.StatusCode}");
                    
                    // Try the UI as a fallback
                    var uiResponse = await httpClient.GetAsync($"{ZipkinBaseUrl}/zipkin/");
                    if (uiResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Zipkin UI is accessible, but API returned an error. Will try to proceed anyway.");
                        return true;
                    }
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking Zipkin connectivity: {ex.Message}");
                return false;
            }
        }
        
        private static async Task GenerateAgentNetworkAsync(IAgentRegistry agentRegistry, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("Generating agent network...");
            
            // Determine number of agents to create
            int numAgents = Random.Next(MinAgents, MaxAgents + 1);
            
            // Create root agent
            var rootAgent = new MockAgent("root-agent", "RootCoordinator", "Main coordinator agent", null);
            Agents.Add(rootAgent);
            await agentRegistry.RegisterAgentAsync(rootAgent);
            
            // Generate agent hierarchy
            await GenerateAgentHierarchyAsync(rootAgent, agentRegistry, 1, numAgents, cancellationToken);
            
            Console.WriteLine($"Generated {Agents.Count} agents with {Tools.Count} tools");
        }
        
        private static async Task GenerateAgentHierarchyAsync(MockAgent parentAgent, IAgentRegistry agentRegistry, int depth, int remainingAgents, CancellationToken cancellationToken = default)
        {
            if (depth > MaxDepth || remainingAgents <= 0)
                return;
            
            int numChildren = Math.Min(Random.Next(1, MaxChildrenPerAgent + 1), remainingAgents);
            remainingAgents -= numChildren;
            
            for (int i = 0; i < numChildren; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                string agentType = AgentTypes[Random.Next(AgentTypes.Length)];
                string id = $"agent-{Agents.Count + 1}";
                string description = $"{agentType} agent at depth {depth}";
                
                var childAgent = new MockAgent(id, agentType, description, parentAgent.Id);
                Agents.Add(childAgent);
                await agentRegistry.RegisterAgentAsync(childAgent);
                
                // Add child to parent
                parentAgent.AddChild(childAgent.Id);
                
                // Generate tools for this agent
                GenerateToolsForAgent(childAgent);
                
                // Recursively generate more children
                await GenerateAgentHierarchyAsync(childAgent, agentRegistry, depth + 1, remainingAgents / numChildren, cancellationToken);
            }
        }
        
        private static void GenerateToolsForAgent(MockAgent agent)
        {
            int numTools = Random.Next(MinToolsPerAgent, MaxToolsPerAgent + 1);
            
            for (int i = 0; i < numTools; i++)
            {
                string toolType = ToolTypes[Random.Next(ToolTypes.Length)];
                string id = $"tool-{Tools.Count + 1}";
                string description = $"{toolType} for {agent.Name}";
                
                var tool = new MockTool(id, toolType, description, agent.Id);
                Tools.Add(tool);
                agent.AddTool(tool);
            }
        }
        
        private static async Task<string> GenerateAgentActivityTraceAsync(IActivityTracker activityTracker, IAgctorLogger logger, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("Generating agent activity trace...");
            string traceId;
            
            using (var rootActivity = activityTracker.StartActivity("CoordinateAgentNetwork"))
            {
                var rootAgent = Agents.First();
                rootActivity.SetAttribute("agent-id", rootAgent.Id);
                rootActivity.SetAttribute("agent-type", rootAgent.ActorType);
                rootActivity.SetAttribute("description", rootAgent.Description);
                
                // For each child agent of the root, create activities
                foreach (var childId in rootAgent.ChildAgentIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var childAgent = Agents.FirstOrDefault(a => a.Id == childId);
                    if (childAgent == null) continue;
                    
                    using (var childActivity = activityTracker.StartActivity($"Task_{childAgent.ActorType}"))
                    {
                        childActivity.SetAttribute("agent-id", childAgent.Id);
                        childActivity.SetAttribute("agent-type", childAgent.ActorType);
                        childActivity.SetAttribute("description", childAgent.Description);
                        
                        // Simulate work
                        await Task.Delay(Random.Next(50, 200), cancellationToken);
                        
                        // For each tool of this agent, create activities
                        foreach (var tool in childAgent.Tools)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            using (var toolActivity = activityTracker.StartActivity($"Use_{tool.Type}"))
                            {
                                toolActivity.SetAttribute("tool-id", tool.Id);
                                toolActivity.SetAttribute("tool-type", tool.Type);
                                toolActivity.SetAttribute("description", tool.Description);
                                
                                // Simulate tool usage
                                await Task.Delay(Random.Next(20, 100), cancellationToken);
                                
                                toolActivity.SetStatus(ActivityStatus.Ok, $"{tool.Type} completed");
                            }
                        }
                        
                        // For each child of this agent, create activities - go deeper in the hierarchy
                        await CreateActivitiesForChildAgents(childAgent, activityTracker, 2, cancellationToken);
                        
                        childActivity.SetStatus(ActivityStatus.Ok, $"Task {childAgent.ActorType} completed");
                    }
                }
                
                rootActivity.SetStatus(ActivityStatus.Ok, "Agent network coordination completed");
                
                // Extract the trace ID
                var context = activityTracker.ExtractContext();
                if (context.TryGetValue("trace-id", out var tid))
                {
                    traceId = tid;
                    logger.Info($"Created trace with ID: {traceId}");
                }
                else
                {
                    // Fall back to a known trace ID if extraction fails
                    traceId = "6525672aa63d82161156e2f2e0e393cd";
                    logger.Warning($"Failed to extract trace ID, using fallback: {traceId}");
                }
            }
            
            // Explicitly force flush of trace data to make sure it gets sent to Zipkin
            try
            {
                Console.WriteLine("Explicitly flushing trace data to Zipkin...");
                
                // Access the TracerProvider from the class-level field
                
                if (CurrentTracerProvider != null)
                {
                    // Force flush with a reasonable timeout
                    Console.WriteLine("Calling ForceFlush on TracerProvider...");
                    var flushResult = CurrentTracerProvider.ForceFlush();
                    Console.WriteLine($"ForceFlush completed");
                    
                    // Add a delay to ensure data is processed
                    Console.WriteLine("Waiting for spans to be exported...");
                    await Task.Delay(3000, cancellationToken);
                }
                else
                {
                    Console.WriteLine("No TracerProvider found, using delay instead");
                    await Task.Delay(2000, cancellationToken);
                }
                
                Console.WriteLine("Waiting for Zipkin to process the trace...");
                await Task.Delay(2000, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error flushing trace data");
            }
            
            return traceId;
        }
        
        // Helper method to recursively create activities for all agents in the hierarchy
        private static async Task CreateActivitiesForChildAgents(MockAgent parentAgent, IActivityTracker activityTracker, int depth, CancellationToken cancellationToken)
        {
            if (depth > MaxDepth || !parentAgent.ChildAgentIds.Any())
                return;
                
            foreach (var childId in parentAgent.ChildAgentIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var childAgent = Agents.FirstOrDefault(a => a.Id == childId);
                if (childAgent == null) continue;
                
                using (var childActivity = activityTracker.StartActivity($"Subtask_{childAgent.ActorType}"))
                {
                    childActivity.SetAttribute("agent-id", childAgent.Id);
                    childActivity.SetAttribute("agent-type", childAgent.ActorType); 
                    childActivity.SetAttribute("description", childAgent.Description);
                    childActivity.SetAttribute("depth", depth.ToString());
                    
                    // Simulate work
                    await Task.Delay(Random.Next(30, 150), cancellationToken);
                    
                    // Process tools for this child agent
                    foreach (var tool in childAgent.Tools)
                    {
                        using (var toolActivity = activityTracker.StartActivity($"Tool_{tool.Type}"))
                        {
                            toolActivity.SetAttribute("tool-id", tool.Id);
                            toolActivity.SetAttribute("tool-type", tool.Type);
                            toolActivity.SetAttribute("description", tool.Description);
                            toolActivity.SetAttribute("owner-agent", childAgent.Id);
                            
                            // Simulate tool usage
                            await Task.Delay(Random.Next(10, 80), cancellationToken);
                            
                            toolActivity.SetStatus(ActivityStatus.Ok, $"{tool.Type} completed");
                        }
                    }
                    
                    // Recursively create activities for this agent's children
                    await CreateActivitiesForChildAgents(childAgent, activityTracker, depth + 1, cancellationToken);
                    
                    childActivity.SetStatus(ActivityStatus.Ok, $"Subtask {childAgent.ActorType} completed");
                }
            }
        }
        
        private static string GenerateAgentHierarchyDiagram()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("graph TD");
            
            // Process the root agent and its descendants
            var rootAgent = Agents.First();
            ProcessAgentForHierarchyDiagram(sb, rootAgent);
            
            // Add CSS classes for styling
            sb.AppendLine("classDef root fill:#f96,stroke:#333,stroke-width:2px");
            sb.AppendLine("classDef agent fill:#bbf,stroke:#333,stroke-width:1px");
            sb.AppendLine($"class {rootAgent.Id} root");
            
            // Collect all non-root agent IDs
            var nonRootAgentIds = Agents.Where(a => a.Id != rootAgent.Id).Select(a => a.Id).ToList();
            if (nonRootAgentIds.Count > 0)
            {
                sb.AppendLine($"class {string.Join(",", nonRootAgentIds)} agent");
            }
            
            return sb.ToString();
        }
        
        private static void ProcessAgentForHierarchyDiagram(StringBuilder sb, MockAgent agent)
        {
            // Add the agent node
            sb.AppendLine($"{agent.Id}[\"{agent.Id}<br/>{agent.ActorType}<br/>{agent.Description}\"]");
            
            // Add tool nodes
            foreach (var tool in agent.Tools)
            {
                sb.AppendLine($"{tool.Id}[\"{tool.Id}<br/>{tool.Type}\"]");
                sb.AppendLine($"{agent.Id} --> {tool.Id}");
            }
            
            // Process all children
            foreach (var childId in agent.ChildAgentIds)
            {
                var child = Agents.FirstOrDefault(a => a.Id == childId);
                if (child != null)
                {
                    ProcessAgentForHierarchyDiagram(sb, child);
                    sb.AppendLine($"{agent.Id} --> {child.Id}");
                }
            }
        }
        
        private static string GenerateMessageFlowDiagram()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("sequenceDiagram");
            
            // Collect all unique participants (agents and tools)
            var participants = new HashSet<string>();
            foreach (var agent in Agents)
            {
                participants.Add(agent.Id);
                foreach (var tool in agent.Tools)
                {
                    participants.Add(tool.Id);
                }
            }
            
            // Create participant definitions
            foreach (var participant in participants)
            {
                string displayName = GetParticipantDisplayName(participant);
                sb.AppendLine($"participant {participant} as \"{displayName}\"");
            }
            
            // Create message flows
            var rootAgent = Agents.First();
            
            // Root agent starts the process
            foreach (var childId in rootAgent.ChildAgentIds)
            {
                var childAgent = Agents.FirstOrDefault(a => a.Id == childId);
                if (childAgent == null) continue;
                
                sb.AppendLine($"{rootAgent.Id}->>+{childId}: Assign task");
                
                // Child agent uses its tools
                foreach (var tool in childAgent.Tools)
                {
                    sb.AppendLine($"{childId}->>+{tool.Id}: Use tool");
                    sb.AppendLine($"{tool.Id}-->>-{childId}: Tool result");
                }
                
                // Child agent delegates to its children
                foreach (var grandchildId in childAgent.ChildAgentIds)
                {
                    var grandchildAgent = Agents.FirstOrDefault(a => a.Id == grandchildId);
                    if (grandchildAgent == null) continue;
                    
                    sb.AppendLine($"{childId}->>+{grandchildId}: Assign subtask");
                    
                    // Grandchild uses its tools
                    foreach (var tool in grandchildAgent.Tools)
                    {
                        sb.AppendLine($"{grandchildId}->>+{tool.Id}: Use tool");
                        sb.AppendLine($"{tool.Id}-->>-{grandchildId}: Tool result");
                    }
                    
                    sb.AppendLine($"{grandchildId}-->>-{childId}: Subtask completed");
                }
                
                sb.AppendLine($"{childId}-->>-{rootAgent.Id}: Task completed");
            }
            
            return sb.ToString();
        }
        
        private static string GetParticipantDisplayName(string participantId)
        {
            var agent = Agents.FirstOrDefault(a => a.Id == participantId);
            if (agent != null)
            {
                return $"{agent.Id} ({agent.ActorType})";
            }
            
            var tool = Tools.FirstOrDefault(t => t.Id == participantId);
            if (tool != null)
            {
                return $"{tool.Id} ({tool.Type})";
            }
            
            return participantId;
        }
        
        private static string GenerateVisualizationHtml(string hierarchyDiagram, string messageFlowDiagram, string traceId, bool zipkinAvailable)
        {
            StringBuilder html = new StringBuilder();
            
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine("    <title>Agctor Agent Network Visualization</title>");
            html.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/mermaid@10.0.0/dist/mermaid.min.js\"></script>");
            html.AppendLine("    <style>");
            html.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
            html.AppendLine("        h1 { color: #333; }");
            html.AppendLine("        .visualization-container { margin-bottom: 40px; }");
            html.AppendLine("        .mermaid { margin-top: 20px; }");
            html.AppendLine("        .info { background-color: #f0f0f0; padding: 15px; border-radius: 5px; margin: 20px 0; }");
            html.AppendLine("        .warning { background-color: #fff3cd; padding: 15px; border-radius: 5px; margin: 20px 0; color: #856404; }");
            html.AppendLine("        .stats { display: flex; flex-wrap: wrap; gap: 20px; margin: 20px 0; }");
            html.AppendLine("        .stat-box { background-color: #e9f7fe; padding: 15px; border-radius: 5px; flex: 1; min-width: 200px; }");
            html.AppendLine("        a { color: #0066cc; text-decoration: none; }");
            html.AppendLine("        a:hover { text-decoration: underline; }");
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("    <h1>Agctor Agent Network Visualization</h1>");
            
            // Stats section
            html.AppendLine("    <div class=\"stats\">");
            html.AppendLine($"        <div class=\"stat-box\"><h3>Agents</h3><p>{Agents.Count} agents in network</p></div>");
            html.AppendLine($"        <div class=\"stat-box\"><h3>Tools</h3><p>{Tools.Count} tools across all agents</p></div>");
            html.AppendLine($"        <div class=\"stat-box\"><h3>Hierarchy Depth</h3><p>Up to {MaxDepth} levels</p></div>");
            html.AppendLine("    </div>");
            
            // Zipkin link or warning
            if (zipkinAvailable)
            {
                html.AppendLine("    <div class=\"info\">");
                html.AppendLine($"        <p>View detailed trace in Zipkin: <a href=\"{ZipkinBaseUrl}/zipkin/traces/{traceId}?serviceName={SourceName}\" target=\"_blank\">{traceId}</a></p>");
                html.AppendLine("        <p><em>Note: Zipkin must be running locally for this link to work</em></p>");
                html.AppendLine("    </div>");
            }
            else
            {
                html.AppendLine("    <div class=\"warning\">");
                html.AppendLine("        <p><strong>Note:</strong> This visualization was generated without Zipkin integration.</p>");
                html.AppendLine("        <p>To enable distributed tracing visualization, ensure Zipkin is running at:</p>");
                html.AppendLine($"        <p><code>{ZipkinBaseUrl}</code></p>");
                html.AppendLine("    </div>");
            }
            
            // Agent hierarchy visualization
            html.AppendLine("    <div class=\"visualization-container\">");
            html.AppendLine("        <h2>Agent Hierarchy</h2>");
            html.AppendLine("        <p>This diagram shows the hierarchical structure of agents and their tools</p>");
            html.AppendLine("        <div class=\"mermaid\">");
            html.AppendLine(hierarchyDiagram);
            html.AppendLine("        </div>");
            html.AppendLine("    </div>");
            
            // Message flow visualization
            html.AppendLine("    <div class=\"visualization-container\">");
            html.AppendLine("        <h2>Message Flow</h2>");
            html.AppendLine("        <p>This diagram shows the communication between agents and tools</p>");
            html.AppendLine("        <div class=\"mermaid\">");
            html.AppendLine(messageFlowDiagram);
            html.AppendLine("        </div>");
            html.AppendLine("    </div>");
            
            // Initialize Mermaid
            html.AppendLine("    <script>");
            html.AppendLine("        mermaid.initialize({ startOnLoad: true, theme: 'default' });");
            html.AppendLine("    </script>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            return html.ToString();
        }
    }
    
    // Simple Mock classes for the demo
    
    public class MockAgent : IAgent
    {
        private readonly List<string> _childIds = new();
        private readonly List<MockTool> _tools = new();
        
        public MockAgent(string id, string actorType, string description, string? parentId)
        {
            Id = id;
            ActorType = actorType;
            Description = description;
            ParentAgentId = parentId;
            Name = actorType;
        }
        
        public string Id { get; }
        public AgentStatus Status => AgentStatus.Idle;
        public string ActorType { get; }
        public ActorState State => ActorState.Active;
        public string? CurrentPrompt => null;
        public string? ParentAgentId { get; private set; }
        public IReadOnlyList<string> ChildAgentIds => _childIds.AsReadOnly();
        public IReadOnlyList<MockTool> Tools => _tools.AsReadOnly();
        public string? Name { get; }
        public string? Description { get; }
        
        public void AddChild(string childId)
        {
            if (!_childIds.Contains(childId))
            {
                _childIds.Add(childId);
            }
        }
        
        public void AddTool(MockTool tool)
        {
            if (!_tools.Contains(tool))
            {
                _tools.Add(tool);
            }
        }
        
        public event EventHandler<AgentStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<SubtaskCompletedEventArgs>? SubtaskCompleted;
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;
        public event EventHandler<ChildAgentSpawnedEventArgs>? ChildAgentSpawned;
        
        // Required interface implementations with minimal functionality
        public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
            => Task.FromResult(envelope);
        
        public Task InitializeAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        
        public Task<bool> TryExecuteAsync(string code, object? context = null, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        
        public Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        
        public Task<string> AssignSubtaskAsync(string subtask, string? childId = null, CancellationToken cancellationToken = default)
            => Task.FromResult($"subtask-{Guid.NewGuid()}");
        
        public Task HandleSubtaskCompletionAsync(string subtaskId, object result, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        
        public Task HandleSubtaskFailureAsync(string subtaskId, Exception exception, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        
        public void SetAgentFactory(IAgentFactory agentFactory) { }
        
        public void SetParentAgentId(string? parentAgentId)
        {
            ParentAgentId = parentAgentId;
        }
        
        public Task ShutdownAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
    
    public class MockTool
    {
        public string Id { get; }
        public string Type { get; }
        public string Description { get; }
        public string OwnerAgentId { get; }
        
        public MockTool(string id, string type, string description, string ownerAgentId)
        {
            Id = id;
            Type = type;
            Description = description;
            OwnerAgentId = ownerAgentId;
        }
    }
    
    public static class LoggerFactory
    {
        public static IAgctorLogger CreateLogger(string name)
        {
            return new ConsoleLogger(name);
        }
    }
    
    public class ConsoleLogger : IAgctorLogger
    {
        private readonly string _name;
        
        public ConsoleLogger(string name)
        {
            _name = name;
        }
        
        public void Trace(string message, params object[] args) => Log("TRACE", FormatMessage(message, args));
        public void Debug(string message, params object[] args) => Log("DEBUG", FormatMessage(message, args));
        public void Info(string message, params object[] args) => Log("INFO", FormatMessage(message, args));
        public void Warning(string message, params object[] args) => Log("WARN", FormatMessage(message, args));
        public void Error(string message, params object[] args) => Log("ERROR", FormatMessage(message, args));
        public void Error(Exception ex, string message, params object[] args) => Log("ERROR", $"{FormatMessage(message, args)} - {ex}");
        public void Critical(string message, params object[] args) => Log("CRITICAL", FormatMessage(message, args));
        public void Critical(Exception ex, string message, params object[] args) => Log("CRITICAL", $"{FormatMessage(message, args)} - {ex}");
        public bool IsEnabled(LogLevel level) => true;
        
        private string FormatMessage(string message, object[] args)
        {
            if (args == null || args.Length == 0)
                return message;
            
            try
            {
                return string.Format(message, args);
            }
            catch
            {
                return message;
            }
        }
        
        private void Log(string level, string message)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] [{_name}] {message}");
        }
    }

    // Custom ActivityTracker that directly uses ActivitySource
    public class ActivitySourceTracker : IActivityTracker
    {
        private readonly ActivitySource _activitySource;
        private readonly IAgctorLogger _logger;

        public ActivitySourceTracker(ActivitySource activitySource, IAgctorLogger logger)
        {
            _activitySource = activitySource;
            _logger = logger;
        }

        public IActivityScope StartActivity(string name, IReadOnlyDictionary<string, string>? context = null)
        {
            System.Diagnostics.ActivityContext? parentContext = null;
            
            // If we have a parent context, extract the trace and span IDs
            if (context != null)
            {
                if (context.TryGetValue("trace-id", out var traceIdStr) && 
                    context.TryGetValue("span-id", out var spanIdStr))
                {
                    // Try to parse manually since we can't use TryParse directly
                    try 
                    {
                        var traceId = ActivityTraceId.CreateFromString(traceIdStr.AsSpan());
                        var spanId = ActivitySpanId.CreateFromString(spanIdStr.AsSpan());
                        parentContext = new System.Diagnostics.ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to parse trace/span IDs: {ex.Message}");
                    }
                }
            }
            
            // Start activity with parent context if available
            Activity? activity;
            if (parentContext.HasValue)
            {
                activity = _activitySource.StartActivity(name, ActivityKind.Internal, parentContext.Value);
            }
            else
            {
                activity = _activitySource.StartActivity(name);
            }
            
            if (activity != null)
            {
                return new ActivityScopeWrapper(activity, _logger);
            }
            
            return new NullActivityScope();
        }

        public void PropagateContext(IDictionary<string, string> headers)
        {
            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                headers["trace-id"] = currentActivity.TraceId.ToHexString();
                headers["span-id"] = currentActivity.SpanId.ToHexString();
                
                // Add additional context if needed
                headers["activity-name"] = currentActivity.DisplayName;
            }
            else
            {
                _logger.Warning("No current activity found when propagating context");
            }
        }

        public IDictionary<string, string> ExtractContext()
        {
            var context = new Dictionary<string, string>();
            var activity = Activity.Current;
            
            if (activity != null)
            {
                context["trace-id"] = activity.TraceId.ToHexString();
                context["span-id"] = activity.SpanId.ToHexString();
                
                // Add all activity tags
                foreach (var tag in activity.Tags)
                {
                    context[tag.Key] = tag.Value;
                }
            }
            else
            {
                _logger.Warning("No current activity found when extracting context");
            }
            
            return context;
        }

        public async Task<IEnumerable<IActivity>> GetTraceActivitiesAsync(string traceId)
        {
            // In a real implementation, this would query the trace storage backend
            // For this demo, we'll just return an empty list since we're focused on
            // sending trace data to Zipkin rather than reading it back
            _logger.Info($"GetTraceActivitiesAsync called for trace ID: {traceId}");
            return Array.Empty<IActivity>();
        }

        // Activity wrapper that implements IActivityScope
        private class ActivityScopeWrapper : IActivityScope
        {
            private readonly Activity _activity;
            private readonly IAgctorLogger _logger;
            private bool _disposed = false;

            public ActivityScopeWrapper(Activity activity, IAgctorLogger logger)
            {
                _activity = activity;
                _logger = logger;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _activity.Dispose();
                    _disposed = true;
                }
            }

            public void RecordEvent(string name, IReadOnlyDictionary<string, object>? attributes = null)
            {
                var tagList = new ActivityTagsCollection();
                if (attributes != null)
                {
                    foreach (var kvp in attributes)
                    {
                        tagList.Add(kvp.Key, kvp.Value);
                    }
                }
                
                _activity.AddEvent(new ActivityEvent(name, DateTimeOffset.UtcNow, tagList));
            }

            public void RecordException(Exception exception)
            {
                var tags = new ActivityTagsCollection
                {
                    { "exception.type", exception.GetType().FullName },
                    { "exception.message", exception.Message },
                    { "exception.stacktrace", exception.StackTrace }
                };
                
                _activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
                _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            }

            public void SetAttribute(string key, string value)
            {
                _activity.SetTag(key, value);
            }

            public void SetStatus(ActivityStatus status, string? description = null)
            {
                // Map ActivityStatus to ActivityStatusCode
                var statusCode = status switch
                {
                    ActivityStatus.Ok => ActivityStatusCode.Ok,
                    ActivityStatus.Error => ActivityStatusCode.Error,
                    _ => ActivityStatusCode.Unset
                };
                
                _activity.SetStatus(statusCode, description);
            }

            public void SetTimelineDetailJson(string? json)
            {
                if (!string.IsNullOrEmpty(json))
                    _activity.SetTag("agctor.timeline.detail", json.Length <= 4096 ? json : json.Substring(0, 4096));
            }
        }

        // Null implementation for when activity creation fails
        private class NullActivityScope : IActivityScope
        {
            public void Dispose() { }
            public void RecordEvent(string name, IReadOnlyDictionary<string, object>? attributes = null) { }
            public void RecordException(Exception exception) { }
            public void SetAttribute(string key, string value) { }
            public void SetStatus(ActivityStatus status, string? description = null) { }
            public void SetTimelineDetailJson(string? json) { }
        }
    }

    // Mock implementation of IActivity interface for the GetTraceActivitiesAsync method
    public class MockActivity : IActivity
    {
        public string Id { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public string? ParentId { get; set; } = string.Empty;
        public string? Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime EndTime { get; set; } = DateTime.UtcNow.AddMilliseconds(100);
        public TimeSpan Duration => EndTime - StartTime;
        public bool HasResult { get; set; } = false;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? TimelineDetailJson { get; set; }
        public IDictionary<string, string> Attributes { get; } = new Dictionary<string, string>();
        public IEnumerable<IActivity> Children { get; } = Array.Empty<IActivity>();
    }
} 