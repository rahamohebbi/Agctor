# Agctor Visualization Layer

This module provides visualization capabilities for the Agctor system, enabling the visual exploration of agent hierarchies, message flows, and distributed traces.

## Overview

The visualization layer in Agctor provides:

1. **Agent Hierarchy Visualization**: Visual representation of parent-child agent relationships
2. **Message Flow Visualization**: Sequence diagrams showing message exchanges between agents and tools
3. **Distributed Trace Integration**: Direct links to external trace viewers like Jaeger and Zipkin
4. **Mermaid Diagram Generation**: Built-in support for generating Mermaid syntax for various diagram types
5. **HTML Integration**: Utilities for embedding visualizations in web applications

## Key Components

### `IVisualizationService`

The core interface for the visualization system, providing methods to:

- Get URLs for external trace viewers
- Build agent hierarchy trees
- Generate message flow diagrams
- Create Mermaid diagrams for both agent hierarchies and message flows

### `VisualizationService`

The default implementation of `IVisualizationService` that:

- Integrates with Jaeger and Zipkin for distributed trace visualization
- Builds agent hierarchy visualizations by traversing parent-child relationships
- Creates message flow diagrams from trace data
- Generates Mermaid diagram syntax for rendering in web UIs

### `VisualizationExtensions`

Provides extension methods for:

- Registering visualization services in the dependency injection container
- Generating HTML fragments with embedded visualizations
- Including necessary CSS and JavaScript for web integration

## Usage

### Setup

Add the visualization service to your dependency injection container:

```csharp
services.AddAgctorVisualization(options => 
{
    options.TraceViewerType = TraceViewerType.Jaeger;
    options.JaegerBaseUrl = "http://localhost:16686";
    options.ZipkinBaseUrl = "http://localhost:9411";
});
```

### Visualizing Agent Hierarchies

Generate an agent hierarchy diagram:

```csharp
// Get the root agent ID
string rootAgentId = "root-agent-123";

// Get the visualization service
var visualizationService = serviceProvider.GetRequiredService<IVisualizationService>();

// Generate a Mermaid diagram for the agent hierarchy
string mermaidDiagram = await visualizationService.GenerateAgentHierarchyMermaidDiagramAsync(rootAgentId);

// Use the diagram in a UI
Console.WriteLine(mermaidDiagram);
```

### Visualizing Message Flows

Generate a message flow diagram:

```csharp
// Get a trace ID from activity tracking
string traceId = activityTracker.GetCurrentTraceId();

// Generate a Mermaid sequence diagram for the message flow
string mermaidDiagram = await visualizationService.GenerateMessageFlowMermaidDiagramAsync(traceId);

// Use the diagram in a UI
Console.WriteLine(mermaidDiagram);
```

### Web Integration

Generate an HTML fragment with visualizations:

```csharp
// Get HTML with visualizations
string html = await visualizationService.GenerateVisualizationHtmlAsync(rootAgentId, traceId);

// In your web page, include the Mermaid JS library and CSS
string page = $@"
<!DOCTYPE html>
<html>
<head>
    <title>Agent Visualization</title>
    {VisualizationExtensions.GetMermaidJsInclude()}
    {VisualizationExtensions.GetVisualizationCss()}
</head>
<body>
    <h1>Agent Visualization</h1>
    {html}
</body>
</html>";
```

## Integration with External Tools

### Jaeger

Jaeger is an open-source, end-to-end distributed tracing system that helps monitor and troubleshoot complex distributed systems. The visualization layer integrates with Jaeger by:

1. Providing direct links to the Jaeger UI for specific traces
2. Allowing exploration of trace data in the Jaeger UI
3. Supporting queries to the Jaeger API to get trace data for visualization

### Zipkin

Zipkin is a distributed tracing system that helps gather timing data needed to troubleshoot latency problems in service architectures. The visualization layer integrates with Zipkin by:

1. Providing direct links to the Zipkin UI for specific traces
2. Supporting exploration of trace data in the Zipkin UI
3. Allowing queries to the Zipkin API to get trace data for visualization

## Customization

You can customize the visualization service by:

1. Implementing a custom `IVisualizationService` for specialized visualization needs
2. Extending the Mermaid diagram generation for additional diagram types
3. Adding support for other trace visualization tools
4. Customizing the HTML and CSS for web integration

## Mermaid Diagrams

The visualization layer uses [Mermaid](https://mermaid-js.github.io/mermaid/) for diagram rendering. Mermaid is a JavaScript-based diagramming and charting tool that renders Markdown-inspired text definitions to create diagrams dynamically.

The system currently supports:

1. **Graph Diagrams** (`graph TD`) for agent hierarchies
2. **Sequence Diagrams** (`sequenceDiagram`) for message flows

## Demo

Run the projects under `Demo/Visualization` and `Demo/VisualizationExample`
to generate HTML diagrams from live agent traces. 