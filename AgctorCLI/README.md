# Agctor CLI Agent Runner

A simple command-line interface for processing prompts through the Agctor agent system. The CLI accepts prompts via command line arguments, dispatches them to a root agent, and prints results to the console.

## Features

- **Command-line prompt processing**: Accept user prompts as command line arguments
- **Root agent dispatch**: Automatically creates and manages a root agent for prompt processing
- **Multiple runtime support**: Supports different actor runtime backends (InMemory, Orleans, Proto.Actor)
- **Dependency injection**: Properly configured DI container with all Agctor services
- **Comprehensive logging**: Detailed logging for monitoring agent operations
- **Error handling**: Graceful error handling with appropriate exit codes
- **Timeout management**: Configurable timeout for long-running operations

## Usage

### Basic Usage
```bash
AgctorCLI.exe "Your prompt here"
```

### With Runtime Selection
```bash
AgctorCLI.exe "Your prompt here" [runtime]
```

### Arguments

- **prompt** (required): The prompt or task to process. Use quotes for multi-word prompts.
- **runtime** (optional): The runtime to use. Defaults to 'InMemory' if not specified.

### Available Runtimes

- **InMemory**: Fully implemented in-memory runtime (default)
- **Orleans**: Microsoft Orleans runtime (placeholder implementation)
- **Proto.Actor**: Proto.Actor runtime (placeholder implementation)

*Note: Only InMemory runtime is fully implemented in this version.*

## Examples

### 1. Business Analysis Tasks

#### Market Research
```bash
# Basic market analysis
dotnet run -- "Analyze current market trends in the technology sector"

# Specific market segment analysis
dotnet run -- "Research emerging trends in artificial intelligence and machine learning markets"

# Competitive analysis
dotnet run -- "Conduct competitor analysis for cloud computing services"
```

#### Financial Analysis
```bash
# Financial report generation
dotnet run -- "Generate quarterly financial analysis report"

# Investment research
dotnet run -- "Analyze investment opportunities in renewable energy sector"

# Risk assessment
dotnet run -- "Evaluate financial risks for expanding into international markets"
```

### 2. Content Creation Tasks

#### Report Generation
```bash
# Executive summary
dotnet run -- "Create executive summary for Q4 business performance"

# Technical documentation
dotnet run -- "Generate technical documentation for API integration"

# Marketing content
dotnet run -- "Develop marketing content strategy for product launch"
```

#### Research Tasks
```bash
# Industry research
dotnet run -- "Research best practices for remote team management"

# Technology evaluation
dotnet run -- "Evaluate pros and cons of different cloud platforms"

# Trend analysis
dotnet run -- "Analyze consumer behavior trends in e-commerce"
```

### 3. Planning and Strategy

#### Project Planning
```bash
# Project roadmap
dotnet run -- "Create project roadmap for mobile app development"

# Resource planning
dotnet run -- "Plan resource allocation for software development team"

# Timeline creation
dotnet run -- "Develop implementation timeline for digital transformation"
```

#### Strategic Analysis
```bash
# SWOT analysis
dotnet run -- "Perform SWOT analysis for entering new market segment"

# Strategic planning
dotnet run -- "Develop strategic plan for company growth over next 3 years"

# Process optimization
dotnet run -- "Analyze and optimize customer service processes"
```

### 4. Using Different Execution Methods

#### Direct .NET CLI
```bash
# Simple task
dotnet run -- "Summarize key points from quarterly earnings report"

# Complex task with specific runtime
dotnet run -- "Create comprehensive business plan for startup" InMemory

# Quick analysis
dotnet run -- "Identify potential cost savings in current operations"
```

#### Windows Batch Script
```bash
# Using the batch script for easier execution
.\run-agent.bat "Analyze customer feedback trends"

# Multiple related tasks
.\run-agent.bat "Research competitor pricing strategies"
.\run-agent.bat "Evaluate market positioning opportunities"
.\run-agent.bat "Develop pricing recommendation strategy"
```

#### PowerShell Script (Advanced)
```powershell
# Using PowerShell with named parameters
.\run-agent.ps1 -Prompt "Create data analysis report for sales performance"

# Specifying runtime explicitly
.\run-agent.ps1 -Prompt "Generate risk assessment matrix" -Runtime "InMemory"

# Batch processing with PowerShell
$prompts = @(
    "Analyze Q1 sales data",
    "Review customer satisfaction metrics", 
    "Evaluate marketing campaign effectiveness"
)

foreach ($prompt in $prompts) {
    Write-Host "Processing: $prompt" -ForegroundColor Yellow
    .\run-agent.ps1 -Prompt $prompt
    Start-Sleep -Seconds 2
}
```

### 5. Complex Multi-Part Tasks

#### Comprehensive Business Analysis
```bash
# This type of complex prompt may trigger child agent creation
dotnet run -- "Create comprehensive business analysis including market research, competitor analysis, financial projections, and strategic recommendations"
```

#### Product Development Planning
```bash
# Multi-faceted product planning
dotnet run -- "Develop complete product launch strategy including market analysis, target audience identification, pricing strategy, and marketing plan"
```

#### Organizational Assessment
```bash
# Complex organizational analysis
dotnet run -- "Conduct organizational assessment covering team structure, process efficiency, technology stack evaluation, and improvement recommendations"
```

### 6. Error Handling Examples

#### Invalid Usage
```bash
# Missing prompt (shows usage)
dotnet run

# Empty prompt (shows usage)
dotnet run -- ""

# Invalid runtime
dotnet run -- "Test prompt" InvalidRuntime
```

#### Expected Output for Errors
```
🤖 Agctor CLI Agent Runner

Usage:
  AgctorCLI.exe "Your prompt here" [runtime]

Arguments:
  prompt   - The prompt or task to process (required, use quotes for multi-word prompts)
  runtime  - The runtime to use (optional, defaults to 'InMemory')

Examples:
  AgctorCLI.exe "Analyze the current market trends"
  AgctorCLI.exe "Generate a report on sales data" InMemory

Available runtimes: InMemory, Orleans, Proto.Actor
Note: Only InMemory runtime is fully implemented in this version.
```

### 7. Automation and Scripting Examples

#### Batch Processing Script (Windows)
```batch
@echo off
echo Starting batch analysis...

call .\run-agent.bat "Analyze Q1 financial performance"
call .\run-agent.bat "Review customer acquisition metrics"
call .\run-agent.bat "Evaluate operational efficiency"
call .\run-agent.bat "Generate executive summary report"

echo Batch analysis completed!
```

#### PowerShell Automation
```powershell
# Automated report generation
$reportTasks = @{
    "Sales" = "Generate sales performance analysis for current quarter"
    "Marketing" = "Analyze marketing campaign ROI and effectiveness"
    "Operations" = "Review operational metrics and identify improvement areas"
    "Finance" = "Create financial summary with key performance indicators"
}

foreach ($category in $reportTasks.Keys) {
    Write-Host "Generating $category report..." -ForegroundColor Green
    $result = .\run-agent.ps1 -Prompt $reportTasks[$category]
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "$category report completed successfully" -ForegroundColor Green
    } else {
        Write-Host "$category report failed" -ForegroundColor Red
    }
}
```

#### Linux/Mac Shell Script
```bash
#!/bin/bash
# Cross-platform script for Unix-like systems

echo "Starting automated analysis..."

prompts=(
    "Analyze market trends in technology sector"
    "Research competitor strategies and positioning"
    "Evaluate customer satisfaction and feedback"
    "Generate strategic recommendations"
)

for prompt in "${prompts[@]}"; do
    echo "Processing: $prompt"
    dotnet run -- "$prompt"
    
    if [ $? -eq 0 ]; then
        echo "✅ Completed: $prompt"
    else
        echo "❌ Failed: $prompt"
    fi
    
    sleep 1
done

echo "Analysis batch completed!"
```

### 8. Integration Examples

#### CI/CD Pipeline Integration
```yaml
# Example GitHub Actions workflow
name: Business Analysis
on:
  schedule:
    - cron: '0 9 * * 1'  # Every Monday at 9 AM

jobs:
  analysis:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      
      - name: Run Weekly Analysis
        run: |
          cd AgctorCLI
          dotnet run -- "Generate weekly business performance analysis"
```

#### PowerShell Module Integration
```powershell
# Create a PowerShell function for easier integration
function Invoke-AgentAnalysis {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Prompt,
        
        [string]$Runtime = "InMemory",
        
        [switch]$Quiet
    )
    
    $originalLocation = Get-Location
    try {
        Set-Location "C:\Path\To\AgctorCLI"
        
        if (-not $Quiet) {
            Write-Host "🤖 Running agent analysis..." -ForegroundColor Cyan
        }
        
        $result = & dotnet run -- $Prompt $Runtime
        return $result
    }
    finally {
        Set-Location $originalLocation
    }
}

# Usage
Invoke-AgentAnalysis -Prompt "Analyze quarterly sales data"
```

## Output

The CLI provides structured output including:

- **Initialization status**: Runtime setup and configuration
- **Agent creation**: Root agent spawning and initialization
- **Processing progress**: Real-time status updates during prompt processing
- **Final result**: Completion status and summary of agent operations
- **Child agent information**: Number of subtasks and child agents created

### Sample Output
```
🤖 Agctor CLI Agent Runner
📝 Prompt: Analyze market trends
⚙️  Runtime: InMemory

🚀 Initializing InMemory runtime...
✅ Runtime initialized successfully
🤖 Creating root agent for prompt processing...
✅ Root agent created: cli-root-308469c513064d80ba5a2af80778da8f
⏳ Processing prompt...
✅ Prompt processing completed successfully

✅ Result:
Prompt processed successfully by agent cli-root-308469c513064d80ba5a2af80778da8f. 
Agent spawned 0 child agents for subtask processing.
```

## Architecture

The CLI Agent Runner follows these key architectural principles:

### Dependency Injection
- Uses Microsoft.Extensions.DependencyInjection for service configuration
- Registers all Agctor services and runtime adapters
- Configures logging and runtime-specific options

### Agent Processing Flow
1. **Validation**: Validates command line arguments
2. **DI Setup**: Configures dependency injection container
3. **Runtime Init**: Initializes the specified actor runtime
4. **Agent Creation**: Spawns a root agent with the user prompt
5. **Processing**: Waits for agent to complete prompt processing
6. **Result Output**: Displays the final result to console
7. **Cleanup**: Properly disposes of resources

### Error Handling
- Graceful handling of missing arguments with usage display
- Runtime availability validation
- Timeout management for long-running operations
- Proper exit codes for scripting integration

## Configuration

The CLI uses the following default configuration:

```csharp
services.AddAgctor(options =>
{
    options.DefaultRuntime = runtimeName;
    options.MaxConcurrentMessages = 100;
    options.EnableDetailedLogging = false; // Keep it simple for CLI
    options.Environment = "CLI";
});
```

### Runtime Configuration
```csharp
await runtime.InitializeAsync(new Dictionary<string, object>
{
    ["Environment"] = "CLI",
    ["MaxConcurrentMessages"] = 50,
    ["EnableMetrics"] = false // Keep overhead low for CLI
});
```

## Building and Running

### Prerequisites
- .NET 8.0 SDK
- AgctorSDK.Core project reference

### Build
```bash
cd AgctorCLI
dotnet build
```

### Run
```bash
dotnet run -- "Your prompt here"
```

### Publish
```bash
dotnet publish -c Release -o ./publish
```

## Integration

The CLI can be easily integrated into scripts and automation workflows:

### Batch Processing
```bash
# Process multiple prompts
AgctorCLI.exe "Analyze Q1 sales data"
AgctorCLI.exe "Generate marketing report"
AgctorCLI.exe "Review competitor analysis"
```

### Error Handling in Scripts
```bash
AgctorCLI.exe "Your prompt" || echo "Processing failed"
```

### Capture Output
```bash
result=$(AgctorCLI.exe "Your prompt")
echo "Agent result: $result"
```

## Future Enhancements

- **Result extraction**: Enhanced result parsing and structured output
- **Configuration files**: Support for external configuration files
- **Interactive mode**: Interactive prompt processing mode
- **Progress indicators**: Visual progress bars for long operations
- **Output formats**: JSON, XML, and other structured output formats
- **Runtime implementations**: Full Orleans and Proto.Actor implementations 