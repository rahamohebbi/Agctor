using System;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.Observability.Visualization;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.ActivityTracking.Logger;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.Messages;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AgctorSDK.VisualizationExample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Agctor Visualization Example ===\n");
            
            Console.WriteLine("1. Run simplified visualization demo");
            Console.WriteLine("2. Run full visualization demo (with DI)");
            Console.WriteLine("3. Run with mock trace data (no OpenTelemetry)");
            Console.WriteLine("4. Create a real trace with visualization");
            Console.WriteLine("5. Ensure Zipkin is running and create a trace");
            Console.Write("Choose an option (1-5): ");
            
            string choice = Console.ReadLine() ?? "1";
            
            if (choice == "1")
            {
                await RunSimplifiedVisualizationDemo();
            }
            else if (choice == "2")
            {
                await RunFullVisualizationDemo();
            }
            else if (choice == "3")
            {
                await RunMockTraceDemo();
            }
            else if (choice == "5")
            {
                // Ensure Zipkin is running before proceeding
                bool zipkinRunning = await EnsureZipkinIsRunningAsync();
                if (zipkinRunning)
                {
                    Console.WriteLine("Zipkin is running. Proceeding with visualization demo.");
                }
                else
                {
                    Console.WriteLine("WARNING: Could not ensure Zipkin is running. The demo will still run but may not be able to export traces to Zipkin.");
                }
                
                await RunRealTraceDemo();
            }
            else
            {
                await RunRealTraceDemo();
            }
        }
        
        static async Task RunSimplifiedVisualizationDemo()
        {
            Console.WriteLine("\n=== Running Simplified Visualization Demo ===\n");
            
            // Create a logger
            var logger = LoggerFactory.CreateLogger("VisualizationExample");
            
            // Create a visualization service directly without dependencies
            var visualizationService = new VisualizationService(
                null!, // No agent registry needed for demo
                null!, // No activity tracker needed for demo
                logger,
                new VisualizationOptions
                {
                    TraceViewerType = TraceViewerType.Jaeger,
                    JaegerBaseUrl = "http://localhost:16686",
                    ZipkinBaseUrl = "http://localhost:9411"
                });
            
            // Create sample agent hierarchy and message flow diagrams
            string hierarchyDiagram = GenerateSampleHierarchyDiagram();
            string messageDiagram = GenerateSampleMessageFlowDiagram();
            
            // Display diagrams
            Console.WriteLine("\nAgent Hierarchy Diagram (Mermaid format):");
            Console.WriteLine(hierarchyDiagram);
            
            Console.WriteLine("\nMessage Flow Diagram (Mermaid format):");
            Console.WriteLine(messageDiagram);
            
            // Generate HTML with both visualizations
            string html = "<div class=\"agctor-visualization\">";
            
            // Add Jaeger link if Jaeger is running
            string mockTraceId = "6525672aa63d82161156e2f2e0e393cd"; // Using a real trace ID from Jaeger
            html += "<div class=\"viz-links\">";
            html += $"<a href=\"{visualizationService.GetTraceViewerUrl(mockTraceId)}\" target=\"_blank\">View Trace in External Viewer</a>";
            html += "<p><em>Note: This is a demo link to an existing but empty trace. In a real application, this would link to a trace containing spans from actual agent interactions.</em></p>";
            html += "</div>";
            
            // Add agent hierarchy visualization
            html += "<div class=\"viz-section\">";
            html += "<h3>Agent Hierarchy</h3>";
            html += "<div class=\"mermaid\">";
            html += hierarchyDiagram;
            html += "</div>";
            html += "</div>";
            
            // Add message flow visualization
            html += "<div class=\"viz-section\">";
            html += "<h3>Message Flow</h3>";
            html += "<div class=\"mermaid\">";
            html += messageDiagram;
            html += "</div>";
            html += "</div>";
            
            html += "</div>";
            
            // Save the HTML to a file for viewing
            string htmlFile = "visualization_example.html";
            File.WriteAllText(htmlFile, 
                "<!DOCTYPE html><html><head><title>Agctor Visualization Example</title>" +
                VisualizationExtensions.GetMermaidJsInclude() +
                VisualizationExtensions.GetVisualizationCss() +
                "</head><body><h1>Agctor Visualization Example</h1>" +
                html +
                "</body></html>");
            
            logger.Info($"HTML saved to: {htmlFile}");
            logger.Info("Open this file in a web browser to see the visualizations rendered.");
            
            // Show trace viewer URL
            string traceViewerUrl = visualizationService.GetTraceViewerUrl(mockTraceId);
            if (!string.IsNullOrEmpty(traceViewerUrl))
            {
                logger.Info($"Trace viewer URL: {traceViewerUrl}");
                logger.Info("Note: Make sure Jaeger is running to use this URL.");
            }
        }
        
        static async Task RunFullVisualizationDemo()
        {
            Console.WriteLine("\n=== Running Full Visualization Demo ===\n");
            
            // 1. Setup DI container with Agctor services including visualization
            var services = new ServiceCollection();
            
            // Add logger
            services.AddSingleton<IAgctorLogger>(LoggerFactory.CreateLogger("VisualizationExample"));
            
            // Add agent registry (required by VisualizationService)
            services.AddSingleton<IAgentRegistry, AgctorSDK.Core.Registry.InMemoryAgentRegistry>();
            
            // Add Agctor core services
            services.AddAgctor(options =>
            {
                options.DefaultRuntime = "InMemory";
                options.MaxConcurrentMessages = 100;
                options.EnableDetailedLogging = true;
                options.Environment = "VisualizationExample";
            });
            
            // Configure OpenTelemetry with Jaeger exporter
            services.AddAgctorOpenTelemetryTracking(options => 
            {
                options.SourceName = "Agctor.VisualizationExample";
                options.EnableJaegerExporter = true;
                
                // Use HTTP collector endpoint instead of UDP agent
                options.JaegerCollectorEndpoint = "http://localhost:14268/api/traces";
                
                // Add debugging information
                Console.WriteLine("Connecting to Jaeger...");
                Console.WriteLine("Jaeger UI should be available at: http://localhost:16686");
                Console.WriteLine("Press Ctrl+C to exit if it takes too long.");
            });
            
            // Create visualization options
            var visualizationOptions = new VisualizationOptions
            {
                TraceViewerType = TraceViewerType.Jaeger,
                JaegerBaseUrl = "http://localhost:16686",
                ZipkinBaseUrl = "http://localhost:9411"
            };
            
            // Register visualization options
            services.AddSingleton(visualizationOptions);
            
            // Register visualization service directly
            services.AddSingleton<IVisualizationService>(sp => new VisualizationService(
                sp.GetRequiredService<IAgentRegistry>(),
                sp.GetRequiredService<IActivityTracker>(),
                sp.GetRequiredService<IAgctorLogger>(),
                visualizationOptions
            ));
            
            var serviceProvider = services.BuildServiceProvider();
            
            // 2. Get the visualization service
            var visualizationService = serviceProvider.GetRequiredService<IVisualizationService>();
            var logger = serviceProvider.GetRequiredService<IAgctorLogger>();
            var activityTracker = serviceProvider.GetRequiredService<IActivityTracker>();
            
            // Get the agent registry to populate with mock agents
            var agentRegistry = serviceProvider.GetRequiredService<IAgentRegistry>();
            
            // Create and register mock agents to demonstrate hierarchy visualization
            var rootAgentId = "root-agent-123";
            var childAgent1Id = "child-agent-1";
            var childAgent2Id = "child-agent-2";
            var grandchildAgentId = "grandchild-agent-1";
            
            // Create mock agents
            var rootAgent = new MockAgent(rootAgentId, "Root Coordinator", null);
            var childAgent1 = new MockAgent(childAgent1Id, "Data Processor", rootAgentId);
            var childAgent2 = new MockAgent(childAgent2Id, "Report Generator", rootAgentId);
            var grandchildAgent = new MockAgent(grandchildAgentId, "Format Processor", childAgent1Id);
            
            // Register the agents
            agentRegistry.RegisterAgentAsync(rootAgent).GetAwaiter().GetResult();
            agentRegistry.RegisterAgentAsync(childAgent1).GetAwaiter().GetResult();
            agentRegistry.RegisterAgentAsync(childAgent2).GetAwaiter().GetResult();
            agentRegistry.RegisterAgentAsync(grandchildAgent).GetAwaiter().GetResult();
            
            // Add children to parent agents
            rootAgent.AddChild(childAgent1Id);
            rootAgent.AddChild(childAgent2Id);
            childAgent1.AddChild(grandchildAgentId);
            
            // 3. Generate visualizations
            try
            {
                // Generate agent hierarchy visualization
                logger.Info($"Generating agent hierarchy visualization for root agent: {rootAgentId}");
                var hierarchyDiagram = await visualizationService.GenerateAgentHierarchyMermaidDiagramAsync(rootAgentId);
                Console.WriteLine("\nAgent Hierarchy Diagram (Mermaid format):");
                Console.WriteLine(hierarchyDiagram);
                
                // Create a real trace with activities
                string traceId;
                using (var rootActivity = activityTracker.StartActivity("CoordinateTask"))
                {
                    rootActivity.SetAttribute("agent-id", rootAgentId);
                    rootActivity.SetAttribute("agent-type", "Root");
                    
                    // Child agent 1 activity
                    using (var child1Activity = activityTracker.StartActivity("ProcessData"))
                    {
                        child1Activity.SetAttribute("agent-id", childAgent1Id);
                        child1Activity.SetAttribute("agent-type", "Processor");
                        
                        // Simulate work
                        await Task.Delay(100);
                        
                        // Grandchild activity
                        using (var grandchildActivity = activityTracker.StartActivity("FormatResults"))
                        {
                            grandchildActivity.SetAttribute("agent-id", grandchildAgentId);
                            grandchildActivity.SetAttribute("agent-type", "Formatter");
                            
                            // Simulate work
                            await Task.Delay(75);
                            
                            grandchildActivity.SetStatus(ActivityStatus.Ok, "Formatting completed");
                        }
                        
                        child1Activity.SetStatus(ActivityStatus.Ok, "Processing completed");
                    }
                    
                    // Child agent 2 activity
                    using (var child2Activity = activityTracker.StartActivity("GenerateReport"))
                    {
                        child2Activity.SetAttribute("agent-id", childAgent2Id);
                        child2Activity.SetAttribute("agent-type", "Generator");
                        
                        // Simulate work
                        await Task.Delay(120);
                        
                        child2Activity.SetStatus(ActivityStatus.Ok, "Report generated");
                    }
                    
                    rootActivity.SetStatus(ActivityStatus.Ok, "Task coordination completed");
                    
                    // Extract the trace ID
                    var context = activityTracker.ExtractContext();
                    if (context.TryGetValue("trace-id", out var tid))
                    {
                        traceId = tid;
                    }
                    else
                    {
                        // Fall back to a known trace ID
                        traceId = "6525672aa63d82161156e2f2e0e393cd";
                    }
                }
                
                // Allow time for the trace to be exported to Jaeger
                await Task.Delay(1000);
                
                // Generate message flow visualization
                logger.Info($"Generating message flow visualization for trace: {traceId}");
                var messageDiagram = await visualizationService.GenerateMessageFlowMermaidDiagramAsync(traceId);
                Console.WriteLine("\nMessage Flow Diagram (Mermaid format):");
                Console.WriteLine(messageDiagram);
                
                // Generate HTML with both visualizations
                logger.Info("Generating HTML with visualizations...");
                var html = new StringBuilder();
                html.AppendLine("<div class=\"agctor-visualization\">");
                
                // Add Jaeger link
                html.AppendLine("<div class=\"viz-links\">");
                html.AppendLine($"<a href=\"{visualizationService.GetTraceViewerUrl(traceId)}\" target=\"_blank\">View Trace in External Viewer</a>");
                html.AppendLine("<p><em>This link leads to actual trace data in Jaeger from the activities performed in this demo.</em></p>");
                html.AppendLine("</div>");
                
                // Add agent hierarchy visualization
                html.AppendLine("<div class=\"viz-section\">");
                html.AppendLine("<h3>Agent Hierarchy</h3>");
                html.AppendLine("<div class=\"mermaid\">");
                html.AppendLine(hierarchyDiagram);
                html.AppendLine("</div>");
                html.AppendLine("</div>");
                
                // Add message flow visualization
                html.AppendLine("<div class=\"viz-section\">");
                html.AppendLine("<h3>Message Flow</h3>");
                html.AppendLine("<div class=\"mermaid\">");
                html.AppendLine(messageDiagram);
                html.AppendLine("</div>");
                html.AppendLine("</div>");
                
                html.AppendLine("</div>");
                
                // Save HTML to a file
                var htmlFile = "visualization_full_example.html";
                System.IO.File.WriteAllText(htmlFile, 
                    "<!DOCTYPE html><html><head><title>Agctor Visualization Example</title>" +
                    VisualizationExtensions.GetMermaidJsInclude() +
                    VisualizationExtensions.GetVisualizationCss() +
                    "</head><body><h1>Agctor Visualization Example</h1>" +
                    html.ToString() +
                    "</body></html>");
                
                logger.Info($"HTML saved to: {htmlFile}");
                logger.Info("Open this file in a web browser to see the visualizations rendered.");
                
                // Get trace viewer URL
                var traceViewerUrl = visualizationService.GetTraceViewerUrl(traceId);
                if (!string.IsNullOrEmpty(traceViewerUrl))
                {
                    logger.Info($"Trace viewer URL: {traceViewerUrl}");
                    logger.Info("Note: Make sure Jaeger is running to use this URL.");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Visualization error: {ex.Message}");
            }
        }
        
        static async Task RunMockTraceDemo()
        {
            Console.WriteLine("\n=== Running Mock Trace Demo ===\n");
            
            // Create a logger
            var logger = LoggerFactory.CreateLogger("VisualizationExample");
            
            // Setup simple DI without OpenTelemetry
            var services = new ServiceCollection();
            services.AddSingleton<IAgctorLogger>(logger);
            services.AddSingleton<IAgentRegistry, AgctorSDK.Core.Registry.InMemoryAgentRegistry>();
            services.AddSingleton<IActivityTracker>(sp => new LoggerActivityTracker(sp.GetRequiredService<IAgctorLogger>()));
            
            // Add Agctor core services
            services.AddAgctor(options =>
            {
                options.DefaultRuntime = "InMemory";
                options.MaxConcurrentMessages = 100;
                options.EnableDetailedLogging = true;
                options.Environment = "VisualizationExample";
            });
            
            // Create visualization options
            var visualizationOptions = new VisualizationOptions
            {
                TraceViewerType = TraceViewerType.Jaeger,
                JaegerBaseUrl = "http://localhost:16686",
                ZipkinBaseUrl = "http://localhost:9411"
            };
            
            // Register visualization options
            services.AddSingleton(visualizationOptions);
            
            // Register visualization service directly
            services.AddSingleton<IVisualizationService>(sp => new VisualizationService(
                sp.GetRequiredService<IAgentRegistry>(),
                sp.GetRequiredService<IActivityTracker>(),
                sp.GetRequiredService<IAgctorLogger>(),
                visualizationOptions
            ));
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the visualization service
            var visualizationService = serviceProvider.GetRequiredService<IVisualizationService>();
            var agentRegistry = serviceProvider.GetRequiredService<IAgentRegistry>();
            
            // Create and register mock agents to demonstrate hierarchy visualization
            var rootAgentId = "root-agent-123";
            var childAgent1Id = "child-agent-1";
            var childAgent2Id = "child-agent-2";
            var grandchildAgentId = "grandchild-agent-1";
            
            // Create mock agents
            var rootAgent = new MockAgent(rootAgentId, "Root Coordinator", null);
            var childAgent1 = new MockAgent(childAgent1Id, "Data Processor", rootAgentId);
            var childAgent2 = new MockAgent(childAgent2Id, "Report Generator", rootAgentId);
            var grandchildAgent = new MockAgent(grandchildAgentId, "Format Processor", childAgent1Id);
            
            // Register the agents
            agentRegistry.RegisterAgentAsync(rootAgent).GetAwaiter().GetResult();
            agentRegistry.RegisterAgentAsync(childAgent1).GetAwaiter().GetResult();
            agentRegistry.RegisterAgentAsync(childAgent2).GetAwaiter().GetResult();
            agentRegistry.RegisterAgentAsync(grandchildAgent).GetAwaiter().GetResult();
            
            // Add children to parent agents
            rootAgent.AddChild(childAgent1Id);
            rootAgent.AddChild(childAgent2Id);
            childAgent1.AddChild(grandchildAgentId);
            
            try
            {
                // Generate agent hierarchy visualization
                logger.Info($"Generating agent hierarchy visualization for root agent: {rootAgentId}");
                var hierarchyDiagram = await visualizationService.GenerateAgentHierarchyMermaidDiagramAsync(rootAgentId);
                Console.WriteLine("\nAgent Hierarchy Diagram (Mermaid format):");
                Console.WriteLine(hierarchyDiagram);
                
                // Try to find an existing trace ID in Jaeger
                Console.WriteLine("\nAttempting to find an existing trace in Jaeger...");
                var traceId = await FindExistingTraceIdAsync();
                
                // Generate a message flow diagram manually (since we're not creating a real trace)
                var messageDiagram = GenerateDetailedMessageFlowDiagram();
                Console.WriteLine("\nMessage Flow Diagram (Mermaid format):");
                Console.WriteLine(messageDiagram);
                
                // Generate HTML with both visualizations
                logger.Info("Generating HTML with visualizations...");
                var html = new StringBuilder();
                html.AppendLine("<div class=\"agctor-visualization\">");
                
                // Add Jaeger link
                html.AppendLine("<div class=\"viz-links\">");
                html.AppendLine($"<a href=\"{visualizationService.GetTraceViewerUrl(traceId)}\" target=\"_blank\">View Trace in External Viewer</a>");
                html.AppendLine("<p><em>Note: This is a mock demo that doesn't create real trace data in Jaeger. You may see a 404 error if you click the link.</em></p>");
                html.AppendLine("<p><em>To see actual traces, <a href=\"http://localhost:16686/search\" target=\"_blank\">go to Jaeger Search</a> to find existing traces.</em></p>");
                html.AppendLine("</div>");
                
                // Add agent hierarchy visualization
                html.AppendLine("<div class=\"viz-section\">");
                html.AppendLine("<h3>Agent Hierarchy</h3>");
                html.AppendLine("<div class=\"mermaid\">");
                html.AppendLine(hierarchyDiagram);
                html.AppendLine("</div>");
                html.AppendLine("</div>");
                
                // Add message flow visualization
                html.AppendLine("<div class=\"viz-section\">");
                html.AppendLine("<h3>Message Flow</h3>");
                html.AppendLine("<div class=\"mermaid\">");
                html.AppendLine(messageDiagram);
                html.AppendLine("</div>");
                html.AppendLine("</div>");
                
                html.AppendLine("</div>");
                
                // Save HTML to a file
                var htmlFile = "visualization_mock_example.html";
                System.IO.File.WriteAllText(htmlFile, 
                    "<!DOCTYPE html><html><head><title>Agctor Visualization Example</title>" +
                    VisualizationExtensions.GetMermaidJsInclude() +
                    VisualizationExtensions.GetVisualizationCss() +
                    "</head><body><h1>Agctor Visualization Example</h1>" +
                    html.ToString() +
                    "</body></html>");
                
                logger.Info($"HTML saved to: {htmlFile}");
                logger.Info("Open this file in a web browser to see the visualizations rendered.");
                
                // Get trace viewer URL
                var traceViewerUrl = visualizationService.GetTraceViewerUrl(traceId);
                if (!string.IsNullOrEmpty(traceViewerUrl))
                {
                    logger.Info($"Trace viewer URL: {traceViewerUrl}");
                    logger.Info("Note: Make sure Jaeger is running to use this URL.");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Visualization error: {ex.Message}");
            }
        }
        
        static string GenerateSampleHierarchyDiagram()
        {
            var sb = new StringBuilder();
            
            string rootAgentId = "root-agent-001";
            string childAgent1Id = "child-agent-001";
            string childAgent2Id = "child-agent-002";
            string grandchildAgentId = "grandchild-agent-001";
            
            sb.AppendLine("graph TD");
            sb.AppendLine($"{rootAgentId}[\"{rootAgentId}<br/>Agent<br/>Coordinate task\"]");
            sb.AppendLine($"{childAgent1Id}[\"{childAgent1Id}<br/>Agent<br/>Process data\"]");
            sb.AppendLine($"{childAgent2Id}[\"{childAgent2Id}<br/>Agent<br/>Generate report\"]");
            sb.AppendLine($"{grandchildAgentId}[\"{grandchildAgentId}<br/>Agent<br/>Format results\"]");
            sb.AppendLine($"{rootAgentId} --> {childAgent1Id}");
            sb.AppendLine($"{rootAgentId} --> {childAgent2Id}");
            sb.AppendLine($"{childAgent1Id} --> {grandchildAgentId}");
            sb.AppendLine("classDef root fill:#f96,stroke:#333,stroke-width:2px");
            sb.AppendLine("classDef agent fill:#bbf,stroke:#333,stroke-width:1px");
            sb.AppendLine($"class {rootAgentId} root");
            sb.AppendLine($"class {childAgent1Id},{childAgent2Id},{grandchildAgentId} agent");
            
            return sb.ToString();
        }
        
        static string GenerateSampleMessageFlowDiagram()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("sequenceDiagram");
            sb.AppendLine("participant root as \"Root Agent\"");
            sb.AppendLine("participant child1 as \"Child Agent 1\"");
            sb.AppendLine("participant child2 as \"Child Agent 2\"");
            sb.AppendLine("participant grandchild as \"Grandchild Agent\"");
            sb.AppendLine("root->>child1: Process data (150ms)");
            sb.AppendLine("root->>child2: Generate report (120ms)");
            sb.AppendLine("child1->>grandchild: Format results (75ms)");
            sb.AppendLine("grandchild-->>child1: Return formatted data");
            sb.AppendLine("child1-->>root: Return processed data");
            sb.AppendLine("child2-->>root: Return generated report");
            
            return sb.ToString();
        }
        
        static string GenerateDetailedMessageFlowDiagram()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("sequenceDiagram");
            sb.AppendLine("participant root as \"Root Agent (Coordinator)\"");
            sb.AppendLine("participant child1 as \"Child Agent 1 (Data Processor)\"");
            sb.AppendLine("participant child2 as \"Child Agent 2 (Report Generator)\"");
            sb.AppendLine("participant grandchild as \"Grandchild Agent (Format Processor)\"");
            sb.AppendLine("Note over root: Task coordination starts");
            sb.AppendLine("root->>child1: Process data (150ms)");
            sb.AppendLine("root->>child2: Generate report (120ms)");
            sb.AppendLine("activate child1");
            sb.AppendLine("activate child2");
            sb.AppendLine("child1->>grandchild: Format results (75ms)");
            sb.AppendLine("activate grandchild");
            sb.AppendLine("Note over grandchild: Formatting...");
            sb.AppendLine("grandchild-->>child1: Return formatted data");
            sb.AppendLine("deactivate grandchild");
            sb.AppendLine("Note over child1: Processing completed");
            sb.AppendLine("child1-->>root: Return processed data");
            sb.AppendLine("deactivate child1");
            sb.AppendLine("Note over child2: Generating report...");
            sb.AppendLine("child2-->>root: Return generated report");
            sb.AppendLine("deactivate child2");
            sb.AppendLine("Note over root: Task coordination completed");
            
            return sb.ToString();
        }
        
        static async Task<string> FindExistingTraceIdAsync()
        {
            // Default trace ID in case we can't find one
            string defaultTraceId = "6525672aa63d82161156e2f2e0e393cd";
            
            try
            {
                // Query Jaeger for available services
                using (var httpClient = new HttpClient())
                {
                    // Set a timeout to avoid hanging
                    httpClient.Timeout = TimeSpan.FromSeconds(5);
                    
                    Console.WriteLine("Querying Jaeger for available services...");
                    var response = await httpClient.GetAsync("http://localhost:16686/api/services");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var servicesJson = await response.Content.ReadAsStringAsync();
                        
                        // Parse the JSON to get a list of services
                        // Simple approach - look for "jaeger-query" or "jaeger-all-in-one"
                        if (servicesJson.Contains("jaeger-query") || servicesJson.Contains("jaeger-all-in-one"))
                        {
                            Console.WriteLine("Found Jaeger service. Querying for traces...");
                            
                            // Query for traces from the last hour
                            var tracesResponse = await httpClient.GetAsync("http://localhost:16686/api/traces?service=jaeger-all-in-one&lookback=1h&limit=1");
                            
                            if (tracesResponse.IsSuccessStatusCode)
                            {
                                var tracesJson = await tracesResponse.Content.ReadAsStringAsync();
                                
                                // Simple extraction - find a traceID in the response
                                int traceIdIndex = tracesJson.IndexOf("\"traceID\":\"");
                                if (traceIdIndex > 0)
                                {
                                    traceIdIndex += 11; // Length of "\"traceID\":\""
                                    int endIndex = tracesJson.IndexOf("\"", traceIdIndex);
                                    if (endIndex > traceIdIndex)
                                    {
                                        string traceId = tracesJson.Substring(traceIdIndex, endIndex - traceIdIndex);
                                        Console.WriteLine($"Found existing trace ID: {traceId}");
                                        return traceId;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error querying Jaeger: {ex.Message}");
            }
            
            Console.WriteLine("Using default trace ID (might not exist in Jaeger)");
            return defaultTraceId;
        }
        
        static async Task<string> CreateActualTraceAsync()
        {
            // Check if Jaeger collector endpoint is accessible
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(2);
                    Console.WriteLine("Checking if Jaeger collector endpoint is accessible...");
                    var response = await httpClient.GetAsync("http://localhost:14268/");
                    Console.WriteLine($"Jaeger collector endpoint status: {(int)response.StatusCode} {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Jaeger collector endpoint check failed: {ex.Message}");
                Console.WriteLine("Will try to use UDP agent endpoint instead or Zipkin as an alternative.");
            }
            
            // Check if Zipkin is accessible
            bool zipkinAvailable = false;
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(2);
                    Console.WriteLine("Checking if Zipkin endpoint is accessible...");
                    var response = await httpClient.GetAsync("http://localhost:9411/api/v2/services");
                    Console.WriteLine($"Zipkin endpoint status: {(int)response.StatusCode} {response.StatusCode}");
                    zipkinAvailable = response.IsSuccessStatusCode;
                    
                    if (zipkinAvailable)
                    {
                        Console.WriteLine("Zipkin API is accessible. Will configure Zipkin exporter.");
                        
                        // Check if this is provided by Jaeger
                        var jaegerCheck = await CheckIfJaegerIsProvidingZipkinEndpoint();
                        if (jaegerCheck)
                        {
                            Console.WriteLine("Note: Zipkin API is being provided by Jaeger's compatibility mode.");
                        }
                        else
                        {
                            Console.WriteLine("A standalone Zipkin instance appears to be running.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Zipkin endpoint check failed: {ex.Message}");
            }
            
            // Better debug information
            Console.WriteLine("Tracing Endpoints:");
            Console.WriteLine("- Jaeger UI: http://localhost:16686");
            Console.WriteLine("- Jaeger HTTP Collector: http://localhost:14268/api/traces");
            Console.WriteLine("- Jaeger UDP Agent: localhost:6831");
            Console.WriteLine("- Zipkin: http://localhost:9411");
            
            // Create a more stable TracerProvider configuration
            var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService("AgctorDemo", serviceInstanceId: Guid.NewGuid().ToString()))
                .AddSource("AgctorDemo")
                .AddConsoleExporter(); // For debugging

            // Try Jaeger approach
            tracerProviderBuilder.AddJaegerExporter(opts =>
            {
                // Primary approach: use UDP agent (generally more reliable)
                opts.AgentHost = "localhost";
                opts.AgentPort = 6831;
                
                // Also try HTTP collector as backup
                opts.Endpoint = new Uri("http://localhost:14268/api/traces");
                
                // Configure for more reliable exports
                opts.MaxPayloadSizeInBytes = 4096;
                
                // Use Batch processor for more reliable exports
                opts.ExportProcessorType = ExportProcessorType.Batch;
                opts.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                {
                    MaxQueueSize = 2048,
                    ScheduledDelayMilliseconds = 5000,
                    ExporterTimeoutMilliseconds = 30000,
                    MaxExportBatchSize = 512
                };
                
                Console.WriteLine("Configured Jaeger exporter with both UDP and HTTP endpoints");
            });
            
            // Also try Zipkin as an alternative
            if (zipkinAvailable)
            {
                tracerProviderBuilder.AddZipkinExporter(opts =>
                {
                    opts.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
                    opts.ExportProcessorType = ExportProcessorType.Batch;
                    opts.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                    {
                        MaxQueueSize = 2048,
                        ScheduledDelayMilliseconds = 5000,
                        ExporterTimeoutMilliseconds = 30000,
                        MaxExportBatchSize = 512
                    };
                    
                    Console.WriteLine("Configured Zipkin exporter");
                });
            }
            
            using var tracerProvider = tracerProviderBuilder.Build();
                
            // Create an ActivitySource that will generate trace data
            var activitySource = new System.Diagnostics.ActivitySource("AgctorDemo");
            string traceId = string.Empty;
            
            Console.WriteLine("Creating a real trace with multiple spans...");
            
            // Track all spans created for later visualization
            var allSpans = new List<(string name, string spanId, string parentId, string agentId, string agentType, TimeSpan duration)>();
            
            // Start a root activity
            using (var rootActivity = activitySource.StartActivity("RootCoordinatorActivity"))
            {
                if (rootActivity != null)
                {
                    // Add some metadata
                    rootActivity.SetTag("agent.id", "root-agent-123");
                    rootActivity.SetTag("agent.type", "Coordinator");
                    
                    var startTime = DateTime.UtcNow;
                    
                    // Run some nested spans to create a complex trace
                    using (var childActivity1 = activitySource.StartActivity("ProcessDataActivity"))
                    {
                        if (childActivity1 != null)
                        {
                            childActivity1.SetTag("agent.id", "child-agent-1");
                            childActivity1.SetTag("agent.type", "Processor");
                            
                            var childStartTime = DateTime.UtcNow;
                            
                            // Simulate work
                            await Task.Delay(150);
                            
                            // Create a grandchild activity
                            using (var grandchildActivity = activitySource.StartActivity("FormatResultsActivity"))
                            {
                                if (grandchildActivity != null)
                                {
                                    grandchildActivity.SetTag("agent.id", "grandchild-agent-1");
                                    grandchildActivity.SetTag("agent.type", "Formatter");
                                    
                                    var grandchildStartTime = DateTime.UtcNow;
                                    
                                    // Simulate work
                                    await Task.Delay(75);
                                    
                                    // Record grandchild span
                                    allSpans.Add((
                                        "FormatResultsActivity",
                                        grandchildActivity.SpanId.ToHexString(),
                                        grandchildActivity.ParentSpanId.ToHexString(),
                                        "grandchild-agent-1",
                                        "Formatter",
                                        DateTime.UtcNow - grandchildStartTime
                                    ));
                                    
                                    // Print activity details for debugging
                                    PrintActivityDetails(grandchildActivity);
                                }
                            }
                            
                            // Record child span
                            allSpans.Add((
                                "ProcessDataActivity",
                                childActivity1.SpanId.ToHexString(),
                                childActivity1.ParentSpanId.ToHexString(),
                                "child-agent-1",
                                "Processor",
                                DateTime.UtcNow - childStartTime
                            ));
                            
                            // Print activity details for debugging
                            PrintActivityDetails(childActivity1);
                        }
                    }
                    
                    // Create another child span in parallel
                    using (var childActivity2 = activitySource.StartActivity("GenerateReportActivity"))
                    {
                        if (childActivity2 != null)
                        {
                            childActivity2.SetTag("agent.id", "child-agent-2");
                            childActivity2.SetTag("agent.type", "Generator");
                            
                            var child2StartTime = DateTime.UtcNow;
                            
                            // Simulate work
                            await Task.Delay(120);
                            
                            // Record child span
                            allSpans.Add((
                                "GenerateReportActivity",
                                childActivity2.SpanId.ToHexString(),
                                childActivity2.ParentSpanId.ToHexString(),
                                "child-agent-2",
                                "Generator",
                                DateTime.UtcNow - child2StartTime
                            ));
                            
                            // Print activity details for debugging
                            PrintActivityDetails(childActivity2);
                        }
                    }
                    
                    // Get the trace ID in the correct format for Jaeger
                    if (rootActivity.Context.TraceId != default)
                    {
                        traceId = rootActivity.Context.TraceId.ToHexString();
                        Console.WriteLine($"Created trace with ID: {traceId}");
                        
                        // Record root span
                        allSpans.Add((
                            "RootCoordinatorActivity",
                            rootActivity.SpanId.ToHexString(),
                            rootActivity.ParentSpanId.ToHexString(),
                            "root-agent-123",
                            "Coordinator",
                            DateTime.UtcNow - startTime
                        ));
                    }
                    
                    // Print activity details for debugging
                    PrintActivityDetails(rootActivity);
                }
            }
            
            if (string.IsNullOrEmpty(traceId))
            {
                Console.WriteLine("Failed to create trace or extract trace ID");
                // Fall back to using FindExistingTraceIdAsync
                return await FindExistingTraceIdAsync();
            }
            
            // Store the collected spans for later visualization
            GlobalTraceData[traceId] = allSpans;
            
            // Explicitly flush the tracer provider to ensure all spans are sent
            Console.WriteLine("Explicitly flushing trace data to Jaeger...");
            tracerProvider.ForceFlush();
            
            // Give Jaeger a moment to process the trace
            Console.WriteLine("Waiting for Jaeger to process the trace...");
            await Task.Delay(2000);
            
            // Verify that the trace was sent to Jaeger
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(5);
                    var response = await httpClient.GetAsync($"http://localhost:16686/api/traces/{traceId}");
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Trace successfully verified in Jaeger!");
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Trace not found in Jaeger (Status: {response.StatusCode})");
                        Console.WriteLine("The visualization link may still work after a delay or you may need to search for it manually.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error verifying trace: {ex.Message}");
            }
            
            return traceId;
        }
        
        static void PrintActivityDetails(Activity activity)
        {
            Console.WriteLine($"Activity.TraceId:            {activity.TraceId}");
            Console.WriteLine($"Activity.SpanId:             {activity.SpanId}");
            Console.WriteLine($"Activity.TraceFlags:         {activity.ActivityTraceFlags}");
            Console.WriteLine($"Activity.ParentSpanId:       {activity.ParentSpanId}");
            Console.WriteLine($"Activity.ActivitySourceName: {activity.Source.Name}");
            Console.WriteLine($"Activity.DisplayName:        {activity.DisplayName}");
            Console.WriteLine($"Activity.Kind:               {activity.Kind}");
            Console.WriteLine($"Activity.StartTime:          {activity.StartTimeUtc}");
            Console.WriteLine($"Activity.Duration:           {activity.Duration}");
            Console.WriteLine("Activity.Tags:");
            foreach (var tag in activity.Tags)
            {
                Console.WriteLine($"    {tag.Key}: {tag.Value}");
            }
            Console.WriteLine("Resource associated with Activity:");
            // System.Diagnostics.Activity doesn't have a GetResource method, so we'll just display some environment info
            Console.WriteLine($"    service.name: AgctorDemo");
            Console.WriteLine($"    service.instance.id: {Environment.MachineName}");
            Console.WriteLine($"    telemetry.sdk.name: opentelemetry");
            Console.WriteLine($"    telemetry.sdk.language: dotnet");
            Console.WriteLine($"    telemetry.sdk.version: 1.6.0");
            Console.WriteLine();
        }
        
        static async Task RunRealTraceDemo()
        {
            Console.WriteLine("\n=== Running Real Trace Demo ===\n");
            
            // Create a logger
            var logger = LoggerFactory.CreateLogger("VisualizationExample");
            
            // Setup simple DI without OpenTelemetry
            var services = new ServiceCollection();
            services.AddSingleton<IAgctorLogger>(logger);
            services.AddSingleton<IAgentRegistry, AgctorSDK.Core.Registry.InMemoryAgentRegistry>();
            services.AddSingleton<IActivityTracker>(sp => new LoggerActivityTracker(sp.GetRequiredService<IAgctorLogger>()));
            
            // Add Agctor core services
            services.AddAgctor(options =>
            {
                options.DefaultRuntime = "InMemory";
                options.MaxConcurrentMessages = 100;
                options.EnableDetailedLogging = true;
                options.Environment = "VisualizationExample";
            });
            
            // Create visualization options
            var visualizationOptions = new VisualizationOptions
            {
                TraceViewerType = TraceViewerType.Jaeger, // Default to Jaeger
                JaegerBaseUrl = "http://localhost:16686",
                ZipkinBaseUrl = "http://localhost:9411"
            };
            
            // Check if Zipkin is accessible, and if so, try to use it first
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(2);
                    var response = await httpClient.GetAsync("http://localhost:9411/api/v2/services");
                    if (response.IsSuccessStatusCode)
                    {
                        logger.Info("Zipkin is accessible, setting as preferred trace viewer");
                        visualizationOptions.TraceViewerType = TraceViewerType.Zipkin;
                    }
                }
            }
            catch
            {
                // Try to start Zipkin if not running
                logger.Info("Zipkin not accessible. You can try option 5 from the main menu to ensure Zipkin is running.");
            }
            
            // Register visualization options
            services.AddSingleton(visualizationOptions);
            
            // Register visualization service directly
            services.AddSingleton<IVisualizationService>(sp => new VisualizationService(
                sp.GetRequiredService<IAgentRegistry>(),
                sp.GetRequiredService<IActivityTracker>(),
                sp.GetRequiredService<IAgctorLogger>(),
                visualizationOptions
            ));
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the visualization service
            var visualizationService = serviceProvider.GetRequiredService<IVisualizationService>();
            var agentRegistry = serviceProvider.GetRequiredService<IAgentRegistry>();
            
            // Create and register mock agents to demonstrate hierarchy visualization
            var rootAgentId = "root-agent-123";
            var childAgent1Id = "child-agent-1";
            var childAgent2Id = "child-agent-2";
            var grandchildAgentId = "grandchild-agent-1";
            
            // Create mock agents
            var rootAgent = new MockAgent(rootAgentId, "Root Coordinator", null);
            var childAgent1 = new MockAgent(childAgent1Id, "Data Processor", rootAgentId);
            var childAgent2 = new MockAgent(childAgent2Id, "Report Generator", rootAgentId);
            var grandchildAgent = new MockAgent(grandchildAgentId, "Format Processor", childAgent1Id);
            
            // Register the agents
            agentRegistry.RegisterAgentAsync(rootAgent).GetAwaiter().GetResult();
            agentRegistry.RegisterAgentAsync(childAgent1).GetAwaiter().GetResult();
            agentRegistry.RegisterAgentAsync(childAgent2).GetAwaiter().GetResult();
            agentRegistry.RegisterAgentAsync(grandchildAgent).GetAwaiter().GetResult();
            
            // Add children to parent agents
            rootAgent.AddChild(childAgent1Id);
            rootAgent.AddChild(childAgent2Id);
            childAgent1.AddChild(grandchildAgentId);
            
            try
            {
                // Generate agent hierarchy visualization
                logger.Info($"Generating agent hierarchy visualization for root agent: {rootAgentId}");
                var hierarchyDiagram = await visualizationService.GenerateAgentHierarchyMermaidDiagramAsync(rootAgentId);
                Console.WriteLine("\nAgent Hierarchy Diagram (Mermaid format):");
                Console.WriteLine(hierarchyDiagram);
                
                // Create a real trace with multiple spans
                Console.WriteLine("\nCreating a real trace that should appear in Jaeger and/or Zipkin...");
                var traceId = await CreateActualTraceAsync();
                
                // Check if the trace exists in the selected viewer
                bool traceExists = await CheckTraceExistsAsync(traceId, visualizationOptions.TraceViewerType);
                
                // If not found in primary viewer, try the alternative
                if (!traceExists && visualizationOptions.TraceViewerType == TraceViewerType.Jaeger)
                {
                    logger.Info("Trace not found in Jaeger, checking Zipkin...");
                    if (await CheckTraceExistsInZipkinAsync(traceId))
                    {
                        logger.Info("Trace found in Zipkin! Switching to Zipkin as trace viewer");
                        visualizationOptions.TraceViewerType = TraceViewerType.Zipkin;
                        traceExists = true;
                    }
                }
                else if (!traceExists && visualizationOptions.TraceViewerType == TraceViewerType.Zipkin)
                {
                    logger.Info("Trace not found in Zipkin, checking Jaeger...");
                    if (await CheckTraceExistsAsync(traceId, TraceViewerType.Jaeger))
                    {
                        logger.Info("Trace found in Jaeger! Switching to Jaeger as trace viewer");
                        visualizationOptions.TraceViewerType = TraceViewerType.Jaeger;
                        traceExists = true;
                    }
                }
                
                // Generate a message flow diagram 
                string messageDiagram;
                
                if (traceExists)
                {
                    // Use the standard method if trace exists in Jaeger
                    messageDiagram = GenerateDetailedMessageFlowDiagram();
                }
                else
                {
                    // Generate from our stored trace data if Jaeger doesn't have it
                    if (GlobalTraceData.TryGetValue(traceId, out var spans))
                    {
                        messageDiagram = GenerateDetailedMessageFlowDiagramFromTrace(traceId, spans);
                    }
                    else
                    {
                        messageDiagram = GenerateDetailedMessageFlowDiagram();
                    }
                }
                
                Console.WriteLine("\nMessage Flow Diagram (Mermaid format):");
                Console.WriteLine(messageDiagram);
                
                // Generate HTML with both visualizations
                logger.Info("Generating HTML with visualizations...");
                var html = new StringBuilder();
                html.AppendLine("<div class=\"agctor-visualization\">");
                
                // Add trace viewer links
                html.AppendLine("<div class=\"viz-links\">");
                
                if (visualizationOptions.TraceViewerType == TraceViewerType.Jaeger)
                {
                    html.AppendLine($"<a href=\"{visualizationService.GetTraceViewerUrl(traceId)}\" target=\"_blank\">View Trace in Jaeger</a>");
                    html.AppendLine($"<a href=\"http://localhost:9411/zipkin/traces/{traceId}\" target=\"_blank\">Try View in Zipkin Instead</a>");
                }
                else
                {
                    html.AppendLine($"<a href=\"{visualizationService.GetTraceViewerUrl(traceId)}\" target=\"_blank\">View Trace in Zipkin</a>");
                    html.AppendLine($"<a href=\"http://localhost:16686/trace/{traceId}\" target=\"_blank\">Try View in Jaeger Instead</a>");
                }
                
                if (traceExists)
                {
                    html.AppendLine("<p><em>This link leads to a real trace with multiple spans that was just created.</em></p>");
                }
                else
                {
                    html.AppendLine("<p><em><strong>Note:</strong> The trace was not found in either Jaeger or Zipkin. This could be due to configuration issues or the tracers not receiving the spans properly.</em></p>");
                    html.AppendLine("<p><em>Below is a visualization based on the trace data that was generated in the application.</em></p>");
                }
                
                html.AppendLine("<p><em>You can also search all traces in <a href=\"http://localhost:16686/search\" target=\"_blank\">Jaeger</a> or <a href=\"http://localhost:9411/zipkin\" target=\"_blank\">Zipkin</a>.</em></p>");
                html.AppendLine("</div>");
                
                // Add agent hierarchy visualization
                html.AppendLine("<div class=\"viz-section\">");
                html.AppendLine("<h3>Agent Hierarchy</h3>");
                html.AppendLine("<div class=\"mermaid\">");
                html.AppendLine(hierarchyDiagram);
                html.AppendLine("</div>");
                html.AppendLine("</div>");
                
                // Add message flow visualization
                html.AppendLine("<div class=\"viz-section\">");
                html.AppendLine("<h3>Message Flow</h3>");
                html.AppendLine("<div class=\"mermaid\">");
                html.AppendLine(messageDiagram);
                html.AppendLine("</div>");
                html.AppendLine("</div>");
                
                // Add a fallback trace summary from the console output
                if (!traceExists)
                {
                    html.AppendLine("<div class=\"viz-section\">");
                    html.AppendLine("<h3>Trace Summary (From Local Data)</h3>");
                    html.AppendLine("<p>Even though the trace was not found in Jaeger, here's a summary of the trace that was generated:</p>");
                    html.AppendLine("<pre>");
                    html.AppendLine($"Trace ID: {traceId}");
                    html.AppendLine("Activities:");
                    
                    if (GlobalTraceData.TryGetValue(traceId, out var spans))
                    {
                        // Display spans organized by parent-child relationship
                        var rootSpans = spans.Where(s => string.IsNullOrEmpty(s.parentId) || s.parentId == "0000000000000000").ToList();
                        foreach (var rootSpan in rootSpans)
                        {
                            html.AppendLine($"- {rootSpan.name} ({rootSpan.agentId}) - Duration: {rootSpan.duration.TotalMilliseconds:0.##}ms");
                            
                            var childSpans = spans.Where(s => s.parentId == rootSpan.spanId).ToList();
                            foreach (var childSpan in childSpans)
                            {
                                html.AppendLine($"  - {childSpan.name} ({childSpan.agentId}) - Duration: {childSpan.duration.TotalMilliseconds:0.##}ms");
                                
                                var grandchildSpans = spans.Where(s => s.parentId == childSpan.spanId).ToList();
                                foreach (var grandchildSpan in grandchildSpans)
                                {
                                    html.AppendLine($"    - {grandchildSpan.name} ({grandchildSpan.agentId}) - Duration: {grandchildSpan.duration.TotalMilliseconds:0.##}ms");
                                }
                            }
                        }
                    }
                    else
                    {
                        html.AppendLine("- RootCoordinatorActivity (root-agent-123)");
                        html.AppendLine("  - ProcessDataActivity (child-agent-1)");
                        html.AppendLine("    - FormatResultsActivity (grandchild-agent-1)");
                        html.AppendLine("  - GenerateReportActivity (child-agent-2)");
                    }
                    
                    html.AppendLine("</pre>");
                    html.AppendLine("</div>");
                    
                    // Add troubleshooting section with info for both tracers
                    html.AppendLine(GetTracingTroubleshootingInfo());
                }
                
                html.AppendLine("</div>");
                
                // Save HTML to a file
                var htmlFile = "visualization_real_trace.html";
                System.IO.File.WriteAllText(htmlFile, 
                    "<!DOCTYPE html><html><head><title>Agctor Visualization Example</title>" +
                    VisualizationExtensions.GetMermaidJsInclude() +
                    VisualizationExtensions.GetVisualizationCss() +
                    GetAdditionalCss() +
                    "</head><body><h1>Agctor Visualization Example</h1>" +
                    html.ToString() +
                    "</body></html>");
                
                logger.Info($"HTML saved to: {htmlFile}");
                logger.Info("Open this file in a web browser to see the visualizations rendered.");
                
                // Get trace viewer URL
                var traceViewerUrl = visualizationService.GetTraceViewerUrl(traceId);
                if (!string.IsNullOrEmpty(traceViewerUrl))
                {
                    logger.Info($"Trace viewer URL: {traceViewerUrl}");
                    
                    if (traceExists)
                    {
                        logger.Info($"Trace was successfully found in {visualizationOptions.TraceViewerType}.");
                    }
                    else
                    {
                        logger.Warning($"The trace was not found in either Jaeger or Zipkin. The links may not work properly.");
                        logger.Info("Try searching for traces manually in the trace viewer UIs.");
                        logger.Info("Local visualization is available in the generated HTML file.");
                    }
                    
                    // Open the HTML file automatically
                    Console.WriteLine("Opening visualization in browser...");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = htmlFile,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Visualization error: {ex.Message}");
            }
        }
        
        static async Task<bool> CheckTraceExistsAsync(string traceId, TraceViewerType viewerType = TraceViewerType.Jaeger)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(5);
                    string url = viewerType == TraceViewerType.Jaeger
                        ? $"http://localhost:16686/api/traces/{traceId}"
                        : $"http://localhost:9411/api/v2/trace/{traceId}";
                        
                    var response = await httpClient.GetAsync(url);
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking if trace exists in {viewerType}: {ex.Message}");
                return false;
            }
        }
        
        static async Task<bool> CheckTraceExistsInZipkinAsync(string traceId)
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(5);
                    
                    // Zipkin API format may be different
                    var response = await httpClient.GetAsync($"http://localhost:9411/api/v2/trace/{traceId}");
                    
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking if trace exists in Zipkin: {ex.Message}");
                return false;
            }
        }
        
        static string GetAdditionalCss()
        {
            return @"
            <style>
                body { font-family: Arial, sans-serif; margin: 20px; }
                h1 { color: #333; }
                .agctor-visualization { margin: 20px 0; }
                .viz-section { margin: 30px 0; border: 1px solid #ddd; padding: 15px; border-radius: 5px; }
                .viz-links { background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin-bottom: 20px; }
                .viz-links a { display: inline-block; margin: 5px 0; padding: 8px 15px; background: #4CAF50; color: white; text-decoration: none; border-radius: 4px; }
                .viz-links a:hover { background: #3e8e41; }
                pre { background-color: #f5f5f5; padding: 10px; border-radius: 4px; overflow-x: auto; }
                ul { padding-left: 20px; }
                .troubleshooting-section { margin-top: 30px; background-color: #f9f9f9; padding: 15px; border-left: 4px solid #ffc107; }
                .troubleshooting-section h3 { color: #856404; }
                .troubleshooting-section pre { background-color: #eaeaea; }
                .troubleshooting-section code { background-color: #eaeaea; padding: 2px 4px; border-radius: 3px; }
            </style>";
        }
        
        static string GenerateDetailedMessageFlowDiagramFromTrace(string traceId, List<(string name, string spanId, string parentId, string agentId, string agentType, TimeSpan duration)> spans)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("sequenceDiagram");
            
            // Get distinct agent IDs to create participants
            var agentGroups = spans
                .GroupBy(s => s.agentId)
                .ToDictionary(g => g.Key, g => g.First().agentType);
            
            // Add participants
            foreach (var agent in agentGroups)
            {
                string participantId = agent.Key.Replace("-", "_").ToLowerInvariant();
                string displayName = agent.Key;
                string agentType = agent.Value;
                
                sb.AppendLine($"participant {participantId} as \"{displayName}<br/>({agentType})\"");
            }
            
            // Sort spans by parent-child relationship to maintain the flow
            var rootSpans = spans.Where(s => string.IsNullOrEmpty(s.parentId) || s.parentId == "0000000000000000").ToList();
            var processedSpanIds = new HashSet<string>();
            
            // Process root spans first
            foreach (var rootSpan in rootSpans)
            {
                sb.AppendLine($"Note over {rootSpan.agentId.Replace("-", "_").ToLowerInvariant()}: {rootSpan.name} starts");
                processedSpanIds.Add(rootSpan.spanId);
                
                // Find direct children of this span
                var children = spans.Where(s => s.parentId == rootSpan.spanId).ToList();
                
                foreach (var child in children)
                {
                    string sourceId = rootSpan.agentId.Replace("-", "_").ToLowerInvariant();
                    string targetId = child.agentId.Replace("-", "_").ToLowerInvariant();
                    
                    // Add message from parent to child
                    sb.AppendLine($"{sourceId}->>+{targetId}: {child.name} ({child.duration.TotalMilliseconds:0}ms)");
                    processedSpanIds.Add(child.spanId);
                    
                    // Find grandchildren
                    var grandchildren = spans.Where(s => s.parentId == child.spanId).ToList();
                    
                    foreach (var grandchild in grandchildren)
                    {
                        string childSourceId = child.agentId.Replace("-", "_").ToLowerInvariant();
                        string grandchildTargetId = grandchild.agentId.Replace("-", "_").ToLowerInvariant();
                        
                        // Add message from child to grandchild
                        sb.AppendLine($"{childSourceId}->>+{grandchildTargetId}: {grandchild.name} ({grandchild.duration.TotalMilliseconds:0}ms)");
                        
                        // Add return message from grandchild to child
                        sb.AppendLine($"{grandchildTargetId}-->>-{childSourceId}: Complete {grandchild.name}");
                        
                        processedSpanIds.Add(grandchild.spanId);
                    }
                    
                    // Add return message from child to parent
                    sb.AppendLine($"{targetId}-->>-{sourceId}: Complete {child.name}");
                }
                
                sb.AppendLine($"Note over {rootSpan.agentId.Replace("-", "_").ToLowerInvariant()}: {rootSpan.name} completes");
            }
            
            return sb.ToString();
        }
        
        static string GetTracingTroubleshootingInfo()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("<div class=\"troubleshooting-section\">");
            sb.AppendLine("<h3>Troubleshooting Trace Viewer Connectivity</h3>");
            
            sb.AppendLine("<p>The application was unable to send trace data to either Jaeger or Zipkin. Here are some steps to troubleshoot:</p>");
            
            // Jaeger Troubleshooting
            sb.AppendLine("<h4>Jaeger Troubleshooting</h4>");
            sb.AppendLine("<ol>");
            sb.AppendLine("<li><strong>Verify Jaeger is running:</strong> <code>docker ps | grep jaeger</code></li>");
            sb.AppendLine("<li><strong>Check Docker port mappings:</strong> Ensure ports 6831/UDP and 14268/TCP are properly exposed and mapped</li>");
            sb.AppendLine("<li><strong>Check network connectivity:</strong> Try <code>telnet localhost 6831</code> or <code>curl http://localhost:14268/</code></li>");
            sb.AppendLine("<li><strong>Restart the Jaeger container:</strong> <code>docker restart jaeger</code></li>");
            sb.AppendLine("<li><strong>Check Docker logs:</strong> <code>docker logs jaeger</code></li>");
            sb.AppendLine("<li><strong>Consider recreating the Jaeger container:</strong>");
            sb.AppendLine("<pre>");
            sb.AppendLine("docker stop jaeger");
            sb.AppendLine("docker rm jaeger");
            sb.AppendLine("docker run -d --name jaeger \\");
            sb.AppendLine("  -e COLLECTOR_ZIPKIN_HTTP_PORT=9411 \\");
            sb.AppendLine("  -p 5775:5775/udp \\");
            sb.AppendLine("  -p 6831:6831/udp \\");
            sb.AppendLine("  -p 6832:6832/udp \\");
            sb.AppendLine("  -p 5778:5778 \\");
            sb.AppendLine("  -p 16686:16686 \\");
            sb.AppendLine("  -p 14268:14268 \\");
            sb.AppendLine("  -p 9411:9411 \\");
            sb.AppendLine("  jaegertracing/all-in-one:latest");
            sb.AppendLine("</pre>");
            sb.AppendLine("</li>");
            sb.AppendLine("</ol>");
            
            // Zipkin Troubleshooting
            sb.AppendLine("<h4>Zipkin Troubleshooting</h4>");
            sb.AppendLine("<ol>");
            sb.AppendLine("<li><strong>Verify Zipkin is running:</strong> <code>docker ps | grep zipkin</code></li>");
            sb.AppendLine("<li><strong>Check Docker port mappings:</strong> Ensure port 9411/TCP is properly exposed and mapped</li>");
            sb.AppendLine("<li><strong>Check network connectivity:</strong> Try <code>curl http://localhost:9411/api/v2/services</code></li>");
            sb.AppendLine("<li><strong>Restart the Zipkin container:</strong> <code>docker restart zipkin</code></li>");
            sb.AppendLine("<li><strong>Check Docker logs:</strong> <code>docker logs zipkin</code></li>");
            sb.AppendLine("<li><strong>Consider running Zipkin if not already running:</strong>");
            sb.AppendLine("<pre>");
            sb.AppendLine("docker run -d --name zipkin -p 9411:9411 openzipkin/zipkin");
            sb.AppendLine("</pre>");
            sb.AppendLine("<p>You can then access Zipkin at <a href=\"http://localhost:9411\" target=\"_blank\">http://localhost:9411</a></p>");
            sb.AppendLine("</li>");
            sb.AppendLine("</ol>");
            
            sb.AppendLine("<p><strong>Further References:</strong></p>");
            sb.AppendLine("<ul>");
            sb.AppendLine("<li><a href=\"https://www.jaegertracing.io/docs/1.37/getting-started/\" target=\"_blank\">Jaeger Getting Started Guide</a></li>");
            sb.AppendLine("<li><a href=\"https://zipkin.io/pages/quickstart.html\" target=\"_blank\">Zipkin Quickstart</a></li>");
            sb.AppendLine("<li><a href=\"https://opentelemetry.io/docs/instrumentation/net/getting-started/\" target=\"_blank\">OpenTelemetry .NET Getting Started</a></li>");
            sb.AppendLine("</ul>");
            
            sb.AppendLine("</div>");
            
            return sb.ToString();
        }
        
        // Global dictionary to store trace data for visualization when Jaeger is unavailable
        static Dictionary<string, List<(string name, string spanId, string parentId, string agentId, string agentType, TimeSpan duration)>> GlobalTraceData = new Dictionary<string, List<(string name, string spanId, string parentId, string agentId, string agentType, TimeSpan duration)>>();
        
        static async Task<bool> EnsureZipkinIsRunningAsync()
        {
            bool zipkinRunning = false;
            
            // First check if Zipkin is accessible on the standard port
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(2);
                    Console.WriteLine("Checking if Zipkin is already running on port 9411...");
                    var response = await httpClient.GetAsync("http://localhost:9411/api/v2/services");
                    zipkinRunning = response.IsSuccessStatusCode;
                    
                    if (zipkinRunning)
                    {
                        Console.WriteLine("Zipkin-compatible API is accessible on port 9411.");
                        
                        // Check if this is provided by Jaeger
                        var jaegerCheck = await CheckIfJaegerIsProvidingZipkinEndpoint();
                        if (jaegerCheck)
                        {
                            Console.WriteLine("Jaeger is providing the Zipkin-compatible API on port 9411.");
                            Console.WriteLine("Will use Jaeger's Zipkin compatibility mode.");
                        }
                        else
                        {
                            Console.WriteLine("A standalone Zipkin instance appears to be running.");
                        }
                        
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"A service on port 9411 returned status code: {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Zipkin is not accessible on port 9411: {ex.Message}");
            }
            
            // Check if Docker is available
            try
            {
                // Check if Docker is running
                var startInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        Console.WriteLine("Docker is running. Checking if port 9411 is already in use...");
                        
                        // Check if port 9411 is already allocated by Docker
                        var checkPort = new ProcessStartInfo
                        {
                            FileName = "docker",
                            Arguments = "ps | grep 9411",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        using var portProcess = Process.Start(checkPort);
                        if (portProcess != null)
                        {
                            string output = await portProcess.StandardOutput.ReadToEndAsync();
                            await portProcess.WaitForExitAsync();
                            
                            if (!string.IsNullOrEmpty(output))
                            {
                                Console.WriteLine("Port 9411 is already allocated by another container:");
                                Console.WriteLine(output);
                                
                                if (output.Contains("jaeger"))
                                {
                                    Console.WriteLine("Jaeger appears to be using port 9411 for Zipkin compatibility.");
                                    Console.WriteLine("Will try to use Jaeger's Zipkin compatibility endpoint.");
                                    
                                    // Check if the Zipkin API is accessible via Jaeger
                                    return await CheckIfJaegerIsProvidingZipkinEndpoint();
                                }
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("Docker is not running. Cannot start Zipkin container.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking or starting Zipkin: {ex.Message}");
            }
            
            return false;
        }
        
        static async Task<bool> CheckIfJaegerIsProvidingZipkinEndpoint()
        {
            try
            {
                // First check if Jaeger UI is running
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(2);
                    var jaegerResponse = await httpClient.GetAsync("http://localhost:16686/api/services");
                    
                    if (jaegerResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Jaeger UI is accessible. Checking Zipkin compatibility API...");
                        
                        // Check if the Zipkin API is accessible
                        var zipkinResponse = await httpClient.GetAsync("http://localhost:9411/api/v2/services");
                        bool zipkinApiWorking = zipkinResponse.IsSuccessStatusCode;
                        
                        if (zipkinApiWorking)
                        {
                            Console.WriteLine("Jaeger's Zipkin compatibility API is working!");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"Jaeger's Zipkin API returned: {zipkinResponse.StatusCode}");
                            
                            // Try a specific Zipkin endpoint that Jaeger might support
                            var zipkinTraceResponse = await httpClient.GetAsync("http://localhost:9411/zipkin/");
                            if (zipkinTraceResponse.IsSuccessStatusCode)
                            {
                                Console.WriteLine("Jaeger's Zipkin UI is accessible.");
                                return true;
                            }
                            
                            return false;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Jaeger UI is not accessible.");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking Jaeger's Zipkin compatibility: {ex.Message}");
                return false;
            }
        }
    }
    
    /// <summary>
    /// A simple mock implementation of IAgent for demonstration purposes.
    /// </summary>
    public class MockAgent : IAgent
    {
        private readonly List<string> _childIds = new List<string>();
        private readonly string _name;
        private readonly string _description;
        private IAgentFactory? _agentFactory;
        
        public MockAgent(string id, string description, string? parentId)
        {
            Id = id;
            _name = id;
            _description = description;
            ParentAgentId = parentId;
        }
        
        public string Id { get; }
        public AgentStatus Status => AgentStatus.Idle;
        public string ActorType => "MockAgent";
        public ActorState State => ActorState.Active;
        public string? CurrentPrompt => null;
        public string? ParentAgentId { get; private set; }
        public IReadOnlyList<string> ChildAgentIds => _childIds.AsReadOnly();
        public string? Name => _name;
        public string? Description => _description;
        
        public void AddChild(string childId)
        {
            if (!_childIds.Contains(childId))
            {
                _childIds.Add(childId);
                ChildAgentSpawned?.Invoke(this, new ChildAgentSpawnedEventArgs(childId, Id, "MockAgent", "MockAgent"));
            }
        }
        
        // Event handlers
        public event EventHandler<AgentStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<SubtaskCompletedEventArgs>? SubtaskCompleted;
        public event EventHandler<ActorStateChangedEventArgs>? StateChanged;
        public event EventHandler<ChildAgentSpawnedEventArgs>? ChildAgentSpawned;
        
        // IAgent method implementations
        public Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            // Just return the envelope as is for this mock implementation
            return Task.FromResult(envelope);
        }
        
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        
        public Task<bool> TryExecuteAsync(string code, object? context = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
        
        public Task ProcessPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        
        public Task<string> AssignSubtaskAsync(string subtask, string? childId = null, CancellationToken cancellationToken = default)
        {
            // Generate a fake subtask ID
            var subtaskId = $"subtask-{Guid.NewGuid().ToString().Substring(0, 8)}";
            return Task.FromResult(subtaskId);
        }
        
        public Task HandleSubtaskCompletionAsync(string subtaskId, object result, CancellationToken cancellationToken = default)
        {
            SubtaskCompleted?.Invoke(this, new SubtaskCompletedEventArgs(subtaskId, Id, result));
            return Task.CompletedTask;
        }
        
        public Task HandleSubtaskFailureAsync(string subtaskId, Exception exception, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        
        public void SetAgentFactory(IAgentFactory agentFactory)
        {
            _agentFactory = agentFactory;
        }
        
        public void SetParentAgentId(string? parentAgentId)
        {
            ParentAgentId = parentAgentId;
        }
        
        public Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
    
    /// <summary>
    /// Simple logger factory for the demo.
    /// </summary>
    public static class LoggerFactory
    {
        /// <summary>
        /// Creates a simple console logger.
        /// </summary>
        public static IAgctorLogger CreateLogger(string name)
        {
            return new ConsoleLogger(name);
        }
    }
    
    /// <summary>
    /// Simple console logger implementation for the demo.
    /// </summary>
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
            
            return string.Format(message, args);
        }
        
        private void Log(string level, string message)
        {
            Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{_name}] {message}");
        }
    }
} 