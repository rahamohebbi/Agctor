# Agctor Visualization Setup Guide

This guide explains how to set up and use the visualization capabilities in your Agctor app.

## Setup

1. **Start Jaeger (Optional but Recommended)**

   For distributed tracing visualization, run Jaeger using Docker:

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

   Jaeger UI will be available at: http://localhost:16686

2. **Configure Visualization in Your App**

   In your application's dependency injection setup, add:

   ```csharp
   // Add visualization services with Jaeger configuration
   services.AddAgctorVisualization(options => 
   {
       options.TraceViewerType = TraceViewerType.Jaeger;
       options.JaegerBaseUrl = "http://localhost:16686";
       options.ZipkinBaseUrl = "http://localhost:9411";
   });
   ```

   If you're using the full Agctor system, you can add observability including visualization with:

   ```csharp
   services.AddAgctorObservability();
   ```

## Usage

### Generate Agent Hierarchy Visualization

```csharp
// Get the visualization service
var visualizationService = serviceProvider.GetRequiredService<IVisualizationService>();

// Generate Mermaid diagram for agent hierarchy
string rootAgentId = "your-root-agent-id";
string diagram = await visualizationService.GenerateAgentHierarchyMermaidDiagramAsync(rootAgentId);
```

### Generate Message Flow Visualization

```csharp
// Get trace ID from activity tracker (in real scenario)
string traceId = activityTracker.GetCurrentTraceId();

// Generate Mermaid sequence diagram for message flow
string diagram = await visualizationService.GenerateMessageFlowMermaidDiagramAsync(traceId);
```

### Generate HTML with Visualizations

```csharp
// Generate HTML with both visualizations
string html = await visualizationService.GenerateVisualizationHtmlAsync(rootAgentId, traceId);

// Save to HTML file with necessary resources
File.WriteAllText("visualization.html", 
    "<!DOCTYPE html><html><head><title>Agctor Visualization</title>" +
    VisualizationExtensions.GetMermaidJsInclude() +
    VisualizationExtensions.GetVisualizationCss() +
    "</head><body>" +
    html +
    "</body></html>");
```

### Run the Visualization Demo

To see a demonstration of the visualization features:

```csharp
await VisualizationDemo.RunVisualizationDemoAsync();
```

This will generate example visualizations and save them to an HTML file.

## Understanding the Visualizations

### Agent Hierarchy

The agent hierarchy visualization shows the parent-child relationships between agents in your Agctor system as a directed graph. The root agent is displayed at the top, with child agents below, connected by arrows.

### Message Flow

The message flow visualization shows the sequence of messages exchanged between agents and tools during execution. This is displayed as a sequence diagram with time flowing from top to bottom.

## Integrating with Web Applications

To integrate visualizations in a web application:

1. Include the Mermaid.js library
2. Include the CSS for styling
3. Generate visualization HTML
4. Add the HTML to your page

Example:

```csharp
@using AgctorSDK.Core.Utils.Observability.Visualization

@inject IVisualizationService VisualizationService

@{
    var rootAgentId = "root-agent-123";
    var traceId = "trace-456";
    var html = await VisualizationService.GenerateVisualizationHtmlAsync(rootAgentId, traceId);
}

<!DOCTYPE html>
<html>
<head>
    <title>Agent Visualization</title>
    @Html.Raw(VisualizationExtensions.GetMermaidJsInclude())
    <style>
        @Html.Raw(VisualizationExtensions.GetVisualizationCss())
    </style>
</head>
<body>
    <h1>Agent Visualization</h1>
    @Html.Raw(html)
</body>
</html>
```

## Running the Example

The `VisualizationExample.cs` file in this repository demonstrates how to use the visualization features. To run it:

1. Make sure Jaeger is running (optional)
2. Build the project: `dotnet build`
3. Run the example: `dotnet run`
4. Open the generated HTML files in a web browser 