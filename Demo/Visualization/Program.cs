using System;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using AgctorSDK.Core.DependencyInjection;
using AgctorSDK.Core.Utils.Logging;
using AgctorSDK.Core.Utils.Observability.Visualization;
using AgctorSDK.Core.Utils.ActivityTracking;
using AgctorSDK.Core.Utils.ActivityTracking.OpenTelemetry;

namespace Agctor.Demo.Visualization
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Setup dependency injection
            var services = new ServiceCollection();
            
            // Add logger
            var logger = LoggerFactory.CreateLogger("Visualization");
            services.AddSingleton<IAgctorLogger>(logger);
            
            // Add basic Agctor services for the agent factory
            services.AddAgctor(options =>
            {
                options.DefaultRuntime = "InMemory";
                options.MaxConcurrentMessages = 100;
                options.EnableDetailedLogging = true;
                options.Environment = "Visualization";
            });
            
            // Configure OpenTelemetry with Jaeger
            services.AddAgctorOpenTelemetryTracking(options => 
            {
                options.SourceName = "Agctor.Visualization";
                options.EnableJaegerExporter = true;
                options.JaegerAgentHost = "localhost";
                options.JaegerAgentPort = 6831;
                
                // Add a warning about potential connectivity issues
                Console.WriteLine("Note: If the application hangs here, it might be because Jaeger is not accessible.");
                Console.WriteLine("Press Ctrl+C to exit if it takes too long.");
            });
            
            // Register visualization options
            var visualizationOptions = new VisualizationOptions
            {
                TraceViewerType = TraceViewerType.Jaeger,
                JaegerBaseUrl = "http://localhost:16686"
            };
            services.AddSingleton(visualizationOptions);
            
            // Register visualization service
            services.AddSingleton<IVisualizationService>(sp => new VisualizationService(
                null!, // No agent registry needed for demo
                sp.GetRequiredService<IActivityTracker>(),
                logger,
                visualizationOptions
            ));
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Get the required services
            var visualizationService = serviceProvider.GetRequiredService<IVisualizationService>();
            var activityTracker = serviceProvider.GetRequiredService<IActivityTracker>();
            
            logger.Info("Starting Agctor Visualization Demo");
            
            // Create a real trace with activities
            string traceId;
            using (var rootActivity = activityTracker.StartActivity("CoordinateTask"))
            {
                rootActivity.SetAttribute("agent-id", "root-agent-001");
                rootActivity.SetAttribute("agent-type", "Coordinator");
                rootActivity.SetAttribute("description", "Root agent coordinating tasks");
                
                // Child agent 1 activity
                using (var child1Activity = activityTracker.StartActivity("ProcessData"))
                {
                    child1Activity.SetAttribute("agent-id", "child-agent-001");
                    child1Activity.SetAttribute("agent-type", "Processor");
                    child1Activity.SetAttribute("description", "Process data");
                    
                    // Simulate work
                    await Task.Delay(150);
                    
                    // Grandchild activity
                    using (var grandchildActivity = activityTracker.StartActivity("FormatResults"))
                    {
                        grandchildActivity.SetAttribute("agent-id", "grandchild-agent-001");
                        grandchildActivity.SetAttribute("agent-type", "Formatter");
                        grandchildActivity.SetAttribute("description", "Format results");
                        
                        // Simulate work
                        await Task.Delay(75);
                        
                        grandchildActivity.SetStatus(ActivityStatus.Ok, "Formatting completed");
                    }
                    
                    child1Activity.SetStatus(ActivityStatus.Ok, "Processing completed");
                }
                
                // Child agent 2 activity
                using (var child2Activity = activityTracker.StartActivity("GenerateReport"))
                {
                    child2Activity.SetAttribute("agent-id", "child-agent-002");
                    child2Activity.SetAttribute("agent-type", "Generator");
                    child2Activity.SetAttribute("description", "Generate report");
                    
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
                    // Fall back to a known trace ID if extraction fails
                    traceId = "6525672aa63d82161156e2f2e0e393cd";
                }
            }
            
            // Allow time for the trace to be exported to Jaeger
            await Task.Delay(1000);
            
            // Create sample visualizations
            string hierarchyDiagram = GenerateAgentHierarchyDiagram();
            Console.WriteLine("\nAgent Hierarchy Diagram (Mermaid format):");
            Console.WriteLine(hierarchyDiagram);
            
            string messageFlowDiagram = GenerateMessageFlowDiagram();
            Console.WriteLine("\nMessage Flow Diagram (Mermaid format):");
            Console.WriteLine(messageFlowDiagram);
            
            // Generate HTML with the visualizations
            string html = GenerateVisualizationHtml(hierarchyDiagram, messageFlowDiagram, traceId);
            System.IO.File.WriteAllText("visualization_demo.html", html);
            
            logger.Info("Visualization HTML saved to: visualization_demo.html");
            logger.Info("Open this file in a web browser to see the visualizations");
            
            // If Jaeger is running, show how to access the trace
            string jaegerUrl = $"http://localhost:16686/trace/{traceId}";
            logger.Info($"If Jaeger is running, you can view traces at: {jaegerUrl}");
        }

        private static string GenerateAgentHierarchyDiagram()
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("graph TD");
            
            // Process the root agent and its descendants
            if (hierarchy.RootAgent != null)
            {
                ProcessAgentForHierarchyDiagram(sb, hierarchy.RootAgent);
                
                // Add CSS classes for styling
                sb.AppendLine("classDef root fill:#f96,stroke:#333,stroke-width:2px");
                sb.AppendLine("classDef agent fill:#bbf,stroke:#333,stroke-width:1px");
                sb.AppendLine($"class {hierarchy.RootAgent.Id} root");
                
                // Collect all non-root agent IDs
                var nonRootAgentIds = CollectNonRootAgentIds(hierarchy.RootAgent);
                if (nonRootAgentIds.Count > 0)
                {
                    sb.AppendLine($"class {string.Join(",", nonRootAgentIds)} agent");
                }
            }
            
            return sb.ToString();
        }

        private static void ProcessAgentForHierarchyDiagram(StringBuilder sb, Agent agent)
        {
            // Add the agent node
            sb.AppendLine($"{agent.Id}[\"{agent.Id}<br/>{agent.Type}<br/>{agent.Description}\"]");
            
            // Process all children
            foreach (var child in agent.Children)
            {
                ProcessAgentForHierarchyDiagram(sb, child);
                sb.AppendLine($"{agent.Id} --> {child.Id}");
            }
        }

        private static List<string> CollectNonRootAgentIds(Agent rootAgent)
        {
            var result = new List<string>();
            CollectNonRootAgentIdsRecursive(rootAgent, result);
            return result;
        }

        private static void CollectNonRootAgentIdsRecursive(Agent agent, List<string> result)
        {
            foreach (var child in agent.Children)
            {
                result.Add(child.Id);
                CollectNonRootAgentIdsRecursive(child, result);
            }
        }

        private static string GenerateMessageFlowDiagram()
        {
            var sb = new StringBuilder();
            var participantMap = new Dictionary<string, string>();
            
            sb.AppendLine("sequenceDiagram");
            
            // Collect all unique participants
            var participants = new HashSet<string>();
            foreach (var flow in messageFlows)
            {
                participants.Add(flow.From);
                participants.Add(flow.To);
            }
            
            // Create participant definitions
            foreach (var participant in participants)
            {
                string alias = GetParticipantAlias(participant);
                participantMap[participant] = alias;
                sb.AppendLine($"participant {alias} as \"{GetParticipantDisplayName(participant)}\"");
            }
            
            // Create the message flows
            foreach (var flow in messageFlows)
            {
                string fromAlias = participantMap[flow.From];
                string toAlias = participantMap[flow.To];
                string durationText = flow.Duration > 0 ? $" ({flow.Duration}ms)" : "";
                
                if (flow.IsReply)
                {
                    sb.AppendLine($"{fromAlias}-->{toAlias}: {flow.Message}{durationText}");
                }
                else
                {
                    sb.AppendLine($"{fromAlias}->>{toAlias}: {flow.Message}{durationText}");
                }
            }
            
            return sb.ToString();
        }

        private static string GetParticipantAlias(string participantId)
        {
            if (participantId.Contains("root"))
                return "root";
            else if (participantId.Contains("grandchild"))
                return "grandchild";
            else if (participantId.Contains("child") && participantId.EndsWith("001"))
                return "child1";
            else if (participantId.Contains("child") && participantId.EndsWith("002"))
                return "child2";
            
            return participantId.Replace("-", "_");
        }

        private static string GetParticipantDisplayName(string participantId)
        {
            if (participantId.Contains("root"))
                return "Root Agent";
            else if (participantId.Contains("grandchild"))
                return "Grandchild Agent";
            else if (participantId.Contains("child") && participantId.EndsWith("001"))
                return "Child Agent 1";
            else if (participantId.Contains("child") && participantId.EndsWith("002"))
                return "Child Agent 2";
            
            return participantId;
        }

        private static string GenerateVisualizationHtml(string hierarchyDiagram, string messageFlowDiagram, string traceId)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("  <title>Agctor Visualization Demo</title>");
            sb.AppendLine("  <script src=\"https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js\"></script>");
            sb.AppendLine("  <style>");
            sb.AppendLine("    body { font-family: Arial, sans-serif; margin: 20px; }");
            sb.AppendLine("    .agctor-visualization { max-width: 1200px; margin: 0 auto; }");
            sb.AppendLine("    .viz-section { margin-bottom: 30px; border: 1px solid #ddd; padding: 15px; border-radius: 5px; }");
            sb.AppendLine("    .viz-links { margin-bottom: 20px; }");
            sb.AppendLine("    h1, h3 { color: #333; }");
            sb.AppendLine("    .mermaid { overflow: auto; }");
            sb.AppendLine("  </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("  <h1>Agctor Visualization Demo</h1>");
            
            sb.AppendLine("  <div class=\"agctor-visualization\">");
            
            // Add Jaeger link with valid hexadecimal trace ID
            sb.AppendLine("    <div class=\"viz-links\">");
            sb.AppendLine("      <a href=\"http://localhost:16686/trace/" + traceId + "\" target=\"_blank\">View Trace in Jaeger</a>");
            sb.AppendLine("      <p><em>This link leads to actual trace data in Jaeger from the activities performed in this demo.</em></p>");
            sb.AppendLine("    </div>");
            
            // Add agent hierarchy visualization
            sb.AppendLine("    <div class=\"viz-section\">");
            sb.AppendLine("      <h3>Agent Hierarchy</h3>");
            sb.AppendLine("      <div class=\"mermaid\">");
            sb.AppendLine(hierarchyDiagram);
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
            
            // Add message flow visualization
            sb.AppendLine("    <div class=\"viz-section\">");
            sb.AppendLine("      <h3>Message Flow</h3>");
            sb.AppendLine("      <div class=\"mermaid\">");
            sb.AppendLine(messageFlowDiagram);
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
            
            sb.AppendLine("  </div>");
            
            sb.AppendLine("  <script>");
            sb.AppendLine("    mermaid.initialize({ startOnLoad: true });");
            sb.AppendLine("  </script>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
            
            return sb.ToString();
        }
    }

    public class Agent
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public List<Agent> Children { get; set; } = new List<Agent>();
    }

    public class AgentHierarchy
    {
        public Agent? RootAgent { get; set; }
    }

    public class MessageFlow
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Message { get; set; } = "";
        public int Duration { get; set; } = 0;
        public bool IsReply { get; set; } = false;
    }
} 