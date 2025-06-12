# Agent Network Visualization Demo

This demo creates a network of 25-50 agents and tools, and generates visualizations showing their relationships and interactions.

## Overview

The demo:
1. Creates a hierarchical network of agents (between 25-50 agents)
2. Each agent has 1-5 tools
3. Creates a trace of simulated agent activities
4. Generates visualization diagrams:
   - Agent hierarchy diagram (showing parent-child relationships)
   - Message flow diagram (showing communication between agents and tools)
5. Outputs an HTML file with interactive visualizations

## Prerequisites

- .NET 8.0 SDK
- Zipkin (for distributed tracing visualization - optional)

## Setup

1. **Start Zipkin (Optional)**

   For distributed tracing visualization, run Zipkin using Docker:

   ```bash
   docker run -d --name zipkin -p 9411:9411 openzipkin/zipkin
   ```

   Zipkin UI will be available at: http://localhost:9411/zipkin/

2. **Build and Run the Demo**

   ```bash
   cd Demo
   dotnet build
   dotnet run --project AgentNetworkVisualization
   ```

## Error Handling

The demo now includes comprehensive error handling for Zipkin connectivity:

1. Before starting, the demo checks if Zipkin is accessible
2. If Zipkin is not available, the demo offers options:
   - Retry the connectivity check
   - Continue without distributed tracing
   - Exit the demo
3. If you continue without tracing, the demo will:
   - Use a local logger-based activity tracker
   - Generate visualizations without external tracing data
   - Show a warning in the HTML output

## Output

The demo generates:
1. `agent_network_visualization.html` - Open this in a web browser to see the visualizations
2. Trace data in Zipkin (if Zipkin is running)

## Visualization Types

### Agent Hierarchy Diagram
Shows the hierarchical structure of agents and their tools using a graph diagram. The root agent is at the top, with child agents below it, connected by arrows.

### Message Flow Diagram
Shows the sequence of messages between agents and tools using a sequence diagram. Time flows from top to bottom.

## Trace Viewer

If Zipkin is running, you can view the full trace of agent activities in the Zipkin UI. The link to the trace is provided in the console output and in the generated HTML file. 