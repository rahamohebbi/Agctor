# Agctor Visualization Demo

This standalone demo shows how to create visualizations for the Agctor framework's agent hierarchy and message flow.

## Overview

Agctor is a framework based on the Actor Model pattern for building agentic systems. Visualizing the agent hierarchy and message flow helps in understanding and debugging your agent-based applications.

The demo provides:

1. A simple visualization of agent hierarchies (parent-child relationships)
2. A message flow diagram showing communications between agents
3. HTML generation for browser-based viewing with Mermaid.js
4. Integration with Jaeger for distributed tracing (optional)

## Running the Demo

1. Ensure you have .NET 8.0 SDK installed

2. Build and run the demo:
   ```bash
   dotnet build
   dotnet run
   ```

3. View the generated HTML file in a browser:
   ```bash
   open visualization_demo.html   # On macOS
   # Or manually open the file in your browser
   ```

## Using Visualizations in Your Agctor App

To add visualization to your own Agctor application:

### 1. Add Required Dependencies

Add the following packages to your project:
```
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Logging
System.Diagnostics.DiagnosticSource (for Jaeger integration)
```

### 2. Collect Agent Hierarchy Data

Implement methods to collect your agent hierarchy, similar to the demo's `CreateSampleAgentHierarchy()` method.

### 3. Collect Message Flow Data

Implement methods to collect message flow data, similar to the demo's `CreateSampleMessageFlow()` method.

### 4. Generate Mermaid Diagrams

Use the provided methods as templates:
- `GenerateAgentHierarchyMermaidDiagram()` for agent hierarchies
- `GenerateMessageFlowMermaidDiagram()` for message flows

### 5. Generate HTML Visualization

Use the `GenerateVisualizationHtml()` method to combine your diagrams into a single HTML file with Mermaid.js rendering.

### 6. Distributed Tracing Integration

For full tracing capabilities:

1. Run Jaeger:
   ```bash
   docker run -d --name jaeger \
     -e COLLECTOR_ZIPKIN_HOST_PORT=:9411 \
     -p 5775:5775/udp \
     -p 6831:6831/udp \
     -p 6832:6832/udp \
     -p 5778:5778 \
     -p 16686:16686 \
     -p 14268:14268 \
     -p 14250:14250 \
     -p 9411:9411 \
     jaegertracing/all-in-one:latest
   ```

2. Access the Jaeger UI at: http://localhost:16686

## Customizing Visualizations

You can customize the visualizations by:

1. Modifying the Mermaid diagram generation logic
2. Customizing the HTML and CSS styles in the `GenerateVisualizationHtml()` method
3. Adding additional metrics or information to the agent nodes or message flows

## Resources

- [Mermaid.js Documentation](https://mermaid.js.org/intro/)
- [Jaeger Tracing Documentation](https://www.jaegertracing.io/docs/latest/)
- [Actor Model](https://en.wikipedia.org/wiki/Actor_model) 