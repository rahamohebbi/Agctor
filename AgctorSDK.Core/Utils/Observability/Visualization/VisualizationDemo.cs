using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using AgctorSDK.Core.Agents;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace AgctorSDK.Core.Utils.Observability.Visualization
{
    /// <summary>
    /// Demonstrates the visualization capabilities of the Agctor system.
    /// </summary>
    public static class VisualizationDemo
    {
        /// <summary>
        /// Runs a demo of the visualization features.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task RunVisualizationDemoAsync()
        {
            Console.WriteLine("=== Agctor Visualization Demo ===\n");
            
            // Create a logger
            var logger = LoggerFactory.CreateLogger("VisualizationDemo");
            
            // Create a visualization service directly
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
            
            // Create a mock agent hierarchy for visualization
            var rootAgentId = "root-agent-001";
            var childAgent1Id = "child-agent-001";
            var childAgent2Id = "child-agent-002";
            var grandchildAgentId = "grandchild-agent-001";
            
            logger.Info("Generating agent hierarchy visualization...");
            
            // Create a sample hierarchy diagram manually
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("graph TD");
            sb.AppendLine($"{rootAgentId}[\"{rootAgentId}<br/>Agent<br/>Coordinate web app\"]");
            sb.AppendLine($"{childAgent1Id}[\"{childAgent1Id}<br/>Agent<br/>Design HTML\"]");
            sb.AppendLine($"{childAgent2Id}[\"{childAgent2Id}<br/>Agent<br/>Implement CSS\"]");
            sb.AppendLine($"{grandchildAgentId}[\"{grandchildAgentId}<br/>Agent<br/>Responsive layout\"]");
            sb.AppendLine($"{rootAgentId} --> {childAgent1Id}");
            sb.AppendLine($"{rootAgentId} --> {childAgent2Id}");
            sb.AppendLine($"{childAgent2Id} --> {grandchildAgentId}");
            sb.AppendLine("classDef root fill:#f96,stroke:#333,stroke-width:2px");
            sb.AppendLine("classDef agent fill:#bbf,stroke:#333,stroke-width:1px");
            sb.AppendLine($"class {rootAgentId} root");
            sb.AppendLine($"class {childAgent1Id} agent");
            sb.AppendLine($"class {childAgent2Id} agent");
            sb.AppendLine($"class {grandchildAgentId} agent");
            
            var hierarchyDiagram = sb.ToString();
            Console.WriteLine("\nAgent Hierarchy Diagram (Mermaid format):");
            Console.WriteLine(hierarchyDiagram);
            
            // Generate message flow visualization
            string mockTraceId = "abc123"; // In a real system, this would be a real trace ID
            logger.Info($"Generating message flow visualization for trace: {mockTraceId}...");
            
            // Create a sample message flow diagram manually
            sb.Clear();
            sb.AppendLine("sequenceDiagram");
            sb.AppendLine("participant root as \"Root Agent\"");
            sb.AppendLine("participant child as \"Child Agent\"");
            sb.AppendLine("participant tool as \"Code Tool\"");
            sb.AppendLine("root->>child: Process subtask (150.5ms)");
            sb.AppendLine("child->>tool: Execute code (75.3ms)");
            sb.AppendLine("tool->>child: Code execution result");
            sb.AppendLine("child->>root: Subtask completed");
            
            var messageDiagram = sb.ToString();
            Console.WriteLine("\nMessage Flow Diagram (Mermaid format):");
            Console.WriteLine(messageDiagram);
            
            // Generate HTML with visualizations
            logger.Info("Generating HTML with visualizations...");
            
            // Create a manual HTML output
            string html = "<div class=\"agctor-visualization\">";
            html += "<div class=\"viz-links\">";
            html += $"<a href=\"{visualizationService.GetTraceViewerUrl(mockTraceId)}\" target=\"_blank\">View Trace in External Viewer</a>";
            html += "</div>";
            
            html += "<div class=\"viz-section\">";
            html += "<h3>Agent Hierarchy</h3>";
            html += "<div class=\"mermaid\">";
            html += hierarchyDiagram;
            html += "</div>";
            html += "</div>";
            
            html += "<div class=\"viz-section\">";
            html += "<h3>Message Flow</h3>";
            html += "<div class=\"mermaid\">";
            html += messageDiagram;
            html += "</div>";
            html += "</div>";
            
            html += "</div>";
            
            // Save the HTML to a file for viewing
            string htmlFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visualization_demo.html");
            File.WriteAllText(htmlFile, 
                "<!DOCTYPE html><html><head><title>Agctor Visualization Demo</title>" +
                VisualizationExtensions.GetMermaidJsInclude() +
                VisualizationExtensions.GetVisualizationCss() +
                "</head><body><h1>Agctor Visualization Demo</h1>" +
                html +
                "</body></html>");
            
            logger.Info($"Demo HTML saved to: {htmlFile}");
            logger.Info("Open this file in a web browser to see the visualizations rendered.");
            
            // Show trace viewer URL
            string traceViewerUrl = visualizationService.GetTraceViewerUrl(mockTraceId);
            if (!string.IsNullOrEmpty(traceViewerUrl))
            {
                logger.Info($"Trace viewer URL: {traceViewerUrl}");
                logger.Info("Note: You need to have Jaeger/Zipkin running to use this URL.");
            }
        }
    }
} 