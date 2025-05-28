@echo off
REM Agctor CLI Agent Runner - Windows Batch Script
REM Usage: run-agent.bat "Your prompt here" [runtime]

if "%~1"=="" (
    echo Error: Prompt is required
    echo Usage: run-agent.bat "Your prompt here" [runtime]
    echo Example: run-agent.bat "Analyze market trends"
    exit /b 1
)

REM Run the CLI with the provided arguments
dotnet run -- %* 