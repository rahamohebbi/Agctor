using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Host-facing entry points for playground flow; implementation lives in Core (<see cref="PersonMemoryMarkdownContextBuilder"/>).
/// </summary>
public static class PlaygroundPersonQueryContextBuilder
{
    internal const int MaxEntities = PersonMemoryMarkdownContextBuilder.MaxEntities;
    internal const int MaxMarkdownFilesPerEntity = PersonMemoryMarkdownContextBuilder.MaxMarkdownFilesPerEntity;
    internal const int MaxAppendixChars = PersonMemoryMarkdownContextBuilder.MaxAppendixChars;

    public static string ParseStrategy(JsonElement? config) =>
        PersonMemoryMarkdownContextBuilder.ParseStrategy(config);

    public static string? ExtractFocusQueryFromUserMessage(string? message) =>
        PersonMemoryMarkdownContextBuilder.ExtractFocusQueryFromUserMessage(message);

    public static Task<string> BuildAppendixAsync(
        ProjectMemoryOperations ops,
        AgentDefinitionSpec querySpec,
        string projectRootFull,
        string? scenarioId,
        string strategy,
        string focusSourceUserMessage,
        CancellationToken cancellationToken) =>
        PersonMemoryMarkdownContextBuilder.BuildAppendixAsync(
            ops,
            querySpec,
            projectRootFull,
            scenarioId,
            strategy,
            focusSourceUserMessage,
            cancellationToken,
            loadedViaLine:
            "Playground person-query: markdown below was read from disk (single-step LLM; use LlmNode toolIds to route via PersonMemoryContextTool).");
}
