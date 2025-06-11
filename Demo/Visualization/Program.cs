using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Agctor.Demo.Visualization
{
    class Program
    {
        static void Main(string[] args)
        {
            // Setup logger
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            var serviceProvider = serviceCollection.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            logger.LogInformation("Starting Agctor Visualization Demo");

            // Create sample agent hierarchy data
            var agentHierarchy = CreateSampleAgentHierarchy();
            var messageFlowData = CreateSampleMessageFlow();

            // Generate visualizations
            string hierarchyDiagram = GenerateAgentHierarchyMermaidDiagram(agentHierarchy);
            string messageFlowDiagram = GenerateMessageFlowMermaidDiagram(messageFlowData);

            // Output diagrams to console
            Console.WriteLine("\nAgent Hierarchy Diagram (Mermaid format):");
            Console.WriteLine(hierarchyDiagram);

            Console.WriteLine("\nMessage Flow Diagram (Mermaid format):");
            Console.WriteLine(messageFlowDiagram);

            // Generate HTML visualization
            string html = GenerateVisualizationHtml(hierarchyDiagram, messageFlowDiagram);
            
            // Save HTML to file
            string htmlFile = "visualization_demo.html";
            File.WriteAllText(htmlFile, html);
            
            logger.LogInformation($"Visualization HTML saved to: {htmlFile}");
            logger.LogInformation("Open this file in a web browser to see the visualizations");

            // If Jaeger is running, show how to access the trace
            string traceId = "1a2b3c4d5e6f7890";
            string jaegerUrl = $"http://localhost:16686/trace/{traceId}";
            logger.LogInformation($"If Jaeger is running, you can view traces at: {jaegerUrl}");
        }

        private static AgentHierarchy CreateSampleAgentHierarchy()
        {
            return new AgentHierarchy
            {
                RootAgent = new Agent
                {
                    Id = "root-agent-001",
                    Type = "Coordinator",
                    Description = "Root agent coordinating tasks",
                    Children = new List<Agent>
                    {
                        new Agent
                        {
                            Id = "child-agent-001",
                            Type = "Processor",
                            Description = "Process data",
                            Children = new List<Agent>
                            {
                                new Agent
                                {
                                    Id = "grandchild-agent-001",
                                    Type = "Formatter",
                                    Description = "Format results",
                                    Children = new List<Agent>()
                                }
                            }
                        },
                        new Agent
                        {
                            Id = "child-agent-002",
                            Type = "Generator",
                            Description = "Generate report",
                            Children = new List<Agent>()
                        }
                    }
                }
            };
        }

        private static List<MessageFlow> CreateSampleMessageFlow()
        {
            return new List<MessageFlow>
            {
                new MessageFlow { From = "root-agent-001", To = "child-agent-001", Message = "Process data", Duration = 150 },
                new MessageFlow { From = "root-agent-001", To = "child-agent-002", Message = "Generate report", Duration = 120 },
                new MessageFlow { From = "child-agent-001", To = "grandchild-agent-001", Message = "Format results", Duration = 75 },
                new MessageFlow { From = "grandchild-agent-001", To = "child-agent-001", Message = "Return formatted data", IsReply = true },
                new MessageFlow { From = "child-agent-001", To = "root-agent-001", Message = "Return processed data", IsReply = true },
                new MessageFlow { From = "child-agent-002", To = "root-agent-001", Message = "Return generated report", IsReply = true }
            };
        }

        private static string GenerateAgentHierarchyMermaidDiagram(AgentHierarchy hierarchy)
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

        private static string GenerateMessageFlowMermaidDiagram(List<MessageFlow> messageFlows)
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

        private static string GenerateVisualizationHtml(string hierarchyDiagram, string messageFlowDiagram)
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
            sb.AppendLine("      <a href=\"http://localhost:16686/trace/1a2b3c4d5e6f7890\" target=\"_blank\">View Trace in Jaeger</a>");
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