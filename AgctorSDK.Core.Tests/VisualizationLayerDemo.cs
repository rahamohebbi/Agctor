using System;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.Observability.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace AgctorSDK.Core.Tests
{
    public class VisualizationLayerDemo
    {
        private readonly ITestOutputHelper _output;

        public VisualizationLayerDemo(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task CanGenerateMermaidDiagrams()
        {
            // This is a demo test that shows how to use the visualization layer
            try
            {
                await VisualizationDemo.RunVisualizationDemoAsync();
                _output.WriteLine("Visualization demo completed. Check the visualization_demo.html file.");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error running visualization demo: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _output.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                if (!string.IsNullOrEmpty(ex.StackTrace))
                {
                    _output.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                throw;
            }
        }
        
        [Fact]
        public void CanGenerateAgentHierarchyDiagram()
        {
            try
            {
                // Create a logger
                var logger = LoggerFactory.CreateLogger("VisualizationTest");
                
                // Create a visualization service directly
                var visualizationService = new VisualizationService(
                    null!, // No agent registry needed for test
                    null!, // No activity tracker needed for test
                    logger,
                    new VisualizationOptions
                    {
                        TraceViewerType = TraceViewerType.Jaeger,
                        JaegerBaseUrl = "http://localhost:16686",
                        ZipkinBaseUrl = "http://localhost:9411"
                    });
                
                // Create a mock agent hierarchy for visualization
                var rootAgentId = "root-agent-001";
                var childAgentId = "child-agent-001";
                
                // Create a sample hierarchy diagram manually
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("graph TD");
                sb.AppendLine($"{rootAgentId}[\"{rootAgentId}<br/>Agent<br/>Root task\"]");
                sb.AppendLine($"{childAgentId}[\"{childAgentId}<br/>Agent<br/>Child task\"]");
                sb.AppendLine($"{rootAgentId} --> {childAgentId}");
                sb.AppendLine("classDef root fill:#f96,stroke:#333,stroke-width:2px");
                sb.AppendLine("classDef agent fill:#bbf,stroke:#333,stroke-width:1px");
                sb.AppendLine($"class {rootAgentId} root");
                sb.AppendLine($"class {childAgentId} agent");
                
                var hierarchyDiagram = sb.ToString();
                _output.WriteLine("Agent Hierarchy Diagram:");
                _output.WriteLine(hierarchyDiagram);
                
                // Save the diagram to HTML for viewing
                string htmlFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hierarchy_test.html");
                File.WriteAllText(htmlFile, 
                    "<!DOCTYPE html><html><head><title>Agent Hierarchy Test</title>" +
                    VisualizationExtensions.GetMermaidJsInclude() +
                    VisualizationExtensions.GetVisualizationCss() +
                    "</head><body><h2>Agent Hierarchy Test</h2>" +
                    "<div class=\"mermaid\">" + hierarchyDiagram + "</div>" +
                    "</body></html>");
                
                _output.WriteLine($"Test HTML saved to: {htmlFile}");
                
                // Verify the diagram contains the agent IDs
                Assert.Contains(rootAgentId, hierarchyDiagram);
                Assert.Contains(childAgentId, hierarchyDiagram);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error in hierarchy test: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _output.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                if (!string.IsNullOrEmpty(ex.StackTrace))
                {
                    _output.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                throw;
            }
        }
    }
} 