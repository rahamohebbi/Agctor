using System;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.Observability.Visualization;

namespace AgctorSDK.VisualizationExample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Agctor Visualization Example ===\n");
            
            Console.WriteLine("1. Run simplified visualization demo");
            Console.WriteLine("2. Run full visualization demo (with DI)");
            Console.Write("Choose an option (1-2): ");
            
            string choice = Console.ReadLine() ?? "1";
            
            if (choice == "1")
            {
                await RunSimplifiedVisualizationDemo();
            }
            else
            {
                await RunFullVisualizationDemo();
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
            string mockTraceId = "trace-123";
            html += "<div class=\"viz-links\">";
            html += $"<a href=\"{visualizationService.GetTraceViewerUrl(mockTraceId)}\" target=\"_blank\">View Trace in External Viewer</a>";
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
            
            // Add Agctor core services
            services.AddAgctor(options =>
            {
                options.DefaultRuntime = "InMemory";
                options.MaxConcurrentMessages = 100;
                options.EnableDetailedLogging = true;
                options.Environment = "VisualizationExample";
            });
            
            // Add visualization services with Jaeger configuration
            AgctorSDK.Core.DependencyInjection.ObservabilityServiceExtensions.AddAgctorVisualization(services, options => 
            {
                options.TraceViewerType = TraceViewerType.Jaeger;
                options.JaegerBaseUrl = "http://localhost:16686";
                options.ZipkinBaseUrl = "http://localhost:9411";
            });
            
            var serviceProvider = services.BuildServiceProvider();
            
            // 2. Get the visualization service
            var visualizationService = serviceProvider.GetRequiredService<IVisualizationService>();
            var logger = LoggerFactory.CreateLogger("VisualizationExample");
            
            // 3. Generate visualizations
            
            // In a real application, you would get the root agent ID from your agent registry
            // For this example, we'll use a dummy ID
            var rootAgentId = "root-agent-123";
            
            try
            {
                // Generate agent hierarchy visualization
                logger.Info($"Generating agent hierarchy visualization for root agent: {rootAgentId}");
                var hierarchyDiagram = await visualizationService.GenerateAgentHierarchyMermaidDiagramAsync(rootAgentId);
                Console.WriteLine("\nAgent Hierarchy Diagram (Mermaid format):");
                Console.WriteLine(hierarchyDiagram);
                
                // In a real application, you would get the trace ID from your activity tracker
                // For this example, we'll use a dummy trace ID
                var traceId = "trace-123";
                
                // Generate message flow visualization
                logger.Info($"Generating message flow visualization for trace: {traceId}");
                var messageDiagram = await visualizationService.GenerateMessageFlowMermaidDiagramAsync(traceId);
                Console.WriteLine("\nMessage Flow Diagram (Mermaid format):");
                Console.WriteLine(messageDiagram);
                
                // Generate HTML with both visualizations
                logger.Info("Generating HTML with visualizations...");
                var html = await visualizationService.GenerateVisualizationHtmlAsync(rootAgentId, traceId);
                
                // Save HTML to a file
                var htmlFile = "visualization_full_example.html";
                System.IO.File.WriteAllText(htmlFile, 
                    "<!DOCTYPE html><html><head><title>Agctor Visualization Example</title>" +
                    VisualizationExtensions.GetMermaidJsInclude() +
                    VisualizationExtensions.GetVisualizationCss() +
                    "</head><body><h1>Agctor Visualization Example</h1>" +
                    html +
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
    }
} 