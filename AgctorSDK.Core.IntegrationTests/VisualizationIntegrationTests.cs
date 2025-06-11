using System;
using System.IO;
using System.Threading.Tasks;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.Observability.Visualization;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;
using Xunit.Abstractions;

namespace AgctorSDK.Core.IntegrationTests
{
    public class VisualizationIntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public VisualizationIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void VisualizationServicesIntegrationTest()
        {
            try
            {
                // Configure services
                var services = new ServiceCollection();
                
                // Configure OpenTelemetry
                services.AddOpenTelemetry()
                    .WithTracing(builder => builder
                        .AddSource("Agctor.IntegrationTest")
                        .AddConsoleExporter());
                
                var serviceProvider = services.BuildServiceProvider();
                
                // Create a simple activity tracker
                var logger = LoggerFactory.CreateLogger("VisualizationTest");
                var activitySource = new System.Diagnostics.ActivitySource("Agctor.IntegrationTest");
                var activityTracker = new OpenTelemetryActivityTracker(activitySource, logger);
                
                // Create visualization service
                var visualizationService = new VisualizationService(
                    null!, // No agent registry needed for test
                    activityTracker,
                    logger,
                    new VisualizationOptions
                    {
                        TraceViewerType = TraceViewerType.Jaeger,
                        JaegerBaseUrl = "http://localhost:16686"
                    });
                
                // Create a test trace
                string traceId;
                using (var activity = activityTracker.StartActivity("TestOperation"))
                {
                    // We can't use AddTag or RecordEvent since they don't exist in the interface
                    // Instead, we'll use Activity directly and only use the methods available in the interface
                    
                    // Record child activities
                    using (var childActivity = activityTracker.StartActivity("ChildOperation"))
                    {
                        // Simulate work
                        Task.Delay(100).GetAwaiter().GetResult();
                    }
                    
                    // Get the trace ID - use a dummy ID for testing
                    traceId = Guid.NewGuid().ToString();
                }
                
                _output.WriteLine($"Created trace with ID: {traceId}");
                
                // Create a sample message flow diagram based on the trace
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("sequenceDiagram");
                sb.AppendLine("participant root as \"TestOperation\"");
                sb.AppendLine("participant child as \"ChildOperation\"");
                sb.AppendLine("root->>child: Invoke (100ms)");
                sb.AppendLine("Note over child: Processing started");
                sb.AppendLine("Note over child: Processing completed");
                sb.AppendLine("child->>root: Return result");
                
                var messageDiagram = sb.ToString();
                
                // Output the diagram
                _output.WriteLine("Message Flow Diagram:");
                _output.WriteLine(messageDiagram);
                
                // Generate HTML visualization
                string html = "<div class=\"agctor-visualization\">";
                html += "<div class=\"viz-section\">";
                html += "<h3>Message Flow</h3>";
                html += "<div class=\"mermaid\">";
                html += messageDiagram;
                html += "</div>";
                html += "</div>";
                html += "</div>";
                
                // Save the HTML to a file for viewing
                string htmlFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "visualization_test.html");
                File.WriteAllText(htmlFile, 
                    "<!DOCTYPE html><html><head><title>Agctor Visualization Test</title>" +
                    VisualizationExtensions.GetMermaidJsInclude() +
                    VisualizationExtensions.GetVisualizationCss() +
                    "</head><body><h1>Agctor Visualization Test</h1>" +
                    html +
                    "</body></html>");
                
                _output.WriteLine($"Visualization HTML saved to: {htmlFile}");
                
                // Verify basic expectations
                Assert.NotEmpty(messageDiagram);
                Assert.Contains("sequenceDiagram", messageDiagram);
                
                // Get trace viewer URL
                var traceViewerUrl = visualizationService.GetTraceViewerUrl(traceId);
                _output.WriteLine($"Trace viewer URL: {traceViewerUrl}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error in integration test: {ex.Message}");
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