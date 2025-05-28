# Agctor CLI Agent Runner - PowerShell Script
# Usage: .\run-agent.ps1 -Prompt "Your prompt here" [-Runtime "InMemory"]

param(
    [Parameter(Mandatory=$true, HelpMessage="The prompt or task to process")]
    [string]$Prompt,
    
    [Parameter(Mandatory=$false, HelpMessage="The runtime to use (InMemory, Orleans, Proto.Actor)")]
    [string]$Runtime = "InMemory"
)

# Validate parameters
if ([string]::IsNullOrWhiteSpace($Prompt)) {
    Write-Host "Error: Prompt cannot be empty" -ForegroundColor Red
    Write-Host "Usage: .\run-agent.ps1 -Prompt 'Your prompt here' [-Runtime 'InMemory']" -ForegroundColor Yellow
    Write-Host "Example: .\run-agent.ps1 -Prompt 'Analyze market trends'" -ForegroundColor Green
    exit 1
}

# Display execution info
Write-Host "Starting Agctor CLI Agent Runner..." -ForegroundColor Cyan
Write-Host "Prompt: $Prompt" -ForegroundColor White
Write-Host "Runtime: $Runtime" -ForegroundColor White
Write-Host ""

# Execute the CLI
try {
    & dotnet run -- $Prompt $Runtime
    $exitCode = $LASTEXITCODE
    
    if ($exitCode -eq 0) {
        Write-Host ""
        Write-Host "Agent processing completed successfully!" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "Agent processing failed with exit code: $exitCode" -ForegroundColor Red
    }
    
    exit $exitCode
}
catch {
    Write-Host "Error executing CLI: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} 