using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Tools;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Playground scenario-flow calls Ollama with a single prompt (no tool loop). For <c>person-query</c>,
/// this builder loads markdown from the scenario entity workspace so the model receives real context.
/// </summary>
public static class PlaygroundPersonQueryContextBuilder
{
    internal const int MaxEntities = 20;
    internal const int MaxMarkdownFilesPerEntity = 40;
    internal const int MaxAppendixChars = 120_000;

    /// <summary>LlmNode.config.contextStrategy — default matches “read scenario people markdown”.</summary>
    public static string ParseStrategy(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } el)
            return "markdown_all";

        if (!el.TryGetProperty("contextStrategy", out var p))
            return "markdown_all";

        var s = p.GetString()?.Trim().ToLowerInvariant();
        return s switch
        {
            "markdown_all" => "markdown_all",
            "markdown_focus" => "markdown_focus",
            "rag" => "rag",
            "graph_rag" => "graph_rag",
            _ => "markdown_all"
        };
    }

    /// <summary>Best-effort token for <see cref="SearchEntitiesAsync"/> substring match (entity key / display name).</summary>
    public static string? ExtractFocusQueryFromUserMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var t = message.Trim();
        var quoted = Regex.Match(t, """["“]([^"”]{2,60})["”]""");
        if (quoted.Success)
            return quoted.Groups[1].Value.Trim();

        foreach (var prefix in new[]
                 {
                     "who is", "who's", "what is", "what's", "tell me about", "do you know",
                     "who was", "what was"
                 })
        {
            if (!t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            t = t[prefix.Length..].Trim(' ', '?', '.', ':', ',', '\t', '\r', '\n');
            break;
        }

        var tokens = Regex.Split(t, @"[^a-zA-Z0-9]+")
            .Where(s => s.Length >= 2)
            .ToList();
        if (tokens.Count == 0)
            return null;

        var cap = tokens.FirstOrDefault(s => s.Any(char.IsUpper));
        return cap ?? tokens.OrderByDescending(s => s.Length).First();
    }

    /// <summary>Builds an appendix block appended to <see cref="ProjectMemoryPersonaLlmRunner.BuildPlaygroundPrompt"/>.</summary>
    public static async Task<string> BuildAppendixAsync(
        ProjectMemoryOperations ops,
        AgentDefinitionSpec querySpec,
        string projectRootFull,
        string? scenarioId,
        string strategy,
        string focusSourceUserMessage,
        CancellationToken cancellationToken)
    {
        var notes = new StringBuilder();
        var effectiveStrategy = strategy;
        if (string.Equals(effectiveStrategy, "rag", StringComparison.OrdinalIgnoreCase)
            || string.Equals(effectiveStrategy, "graph_rag", StringComparison.OrdinalIgnoreCase))
        {
            notes.AppendLine(
                $"Note: contextStrategy '{strategy}' is not wired yet; loaded on-disk markdown like markdown_all.");
            effectiveStrategy = "markdown_all";
        }

        var entityWorkspace = PersonaScenarioScope.GetEntityWorkspaceRoot(projectRootFull, scenarioId);
        if (!PersonaScenarioScope.IsUnderProjectRoot(projectRootFull, entityWorkspace))
        {
            return """
                   ---
                   Playground person-query: entity workspace path is invalid; cannot load markdown.

                   """;
        }

        string? searchQuery = null;
        if (string.Equals(effectiveStrategy, "markdown_focus", StringComparison.OrdinalIgnoreCase))
        {
            searchQuery = ExtractFocusQueryFromUserMessage(focusSourceUserMessage);
            if (string.IsNullOrWhiteSpace(searchQuery))
                effectiveStrategy = "markdown_all";
        }

        IReadOnlyList<EntitySearchHit> hits;
        try
        {
            hits = await ops.SearchEntitiesAsync(projectRootFull, entityWorkspace, searchQuery, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return "---\nPlayground person-query: failed to list entities — " + ex.Message + "\n";
        }

        if (searchQuery is not null && hits.Count == 0)
        {
            hits = await ops.SearchEntitiesAsync(projectRootFull, entityWorkspace, null, cancellationToken)
                .ConfigureAwait(false);
            notes.AppendLine("(markdown_focus: no entity matched the inferred focus text; included all scenario entities.)");
        }

        if (hits.Count == 0)
        {
            var head = new StringBuilder();
            head.AppendLine("---");
            head.AppendLine("Playground person-query: no entities found under this workspace.");
            head.AppendLine("Add or ingest people (markdown under `people/<entityKey>/`) for this scenario, then retry.");
            if (notes.Length > 0)
                head.AppendLine(notes.ToString().TrimEnd());
            return head.ToString().TrimEnd() + "\n";
        }

        var body = new StringBuilder();
        foreach (var h in hits.Take(MaxEntities))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var block = await ReadMarkdownBundleForEntityAsync(
                    ops,
                    querySpec,
                    projectRootFull,
                    entityWorkspace,
                    h.EntityKey,
                    cancellationToken)
                .ConfigureAwait(false);
            body.Append("### ").Append(h.EntityKey).AppendLine();
            body.AppendLine(block.TrimEnd());
            body.AppendLine();
        }

        var combined = body.ToString().TrimEnd();
        if (combined.Length > MaxAppendixChars)
        {
            notes.AppendLine(
                $"(Context truncated to {MaxAppendixChars} characters — narrow with markdown_focus or fewer entities.)");
            combined = combined[..MaxAppendixChars];
        }

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("Playground person-query: markdown below was read from disk (HTTP playground does not execute tools).");
        sb.AppendLine("Use ONLY this text as factual context. Answer the latest user message in plain text.");
        if (notes.Length > 0)
            sb.AppendLine(notes.ToString().TrimEnd());
        sb.AppendLine();
        sb.AppendLine(combined);
        return sb.ToString().TrimEnd();
    }

    private static async Task<string> ReadMarkdownBundleForEntityAsync(
        ProjectMemoryOperations ops,
        AgentDefinitionSpec querySpec,
        string projectRootFull,
        string entityWorkspace,
        string entityKey,
        CancellationToken cancellationToken)
    {
        var peopleDir = Path.Combine(entityWorkspace, "people", entityKey);
        if (!PersonaScenarioScope.IsUnderProjectRoot(projectRootFull, peopleDir) || !Directory.Exists(peopleDir))
            return "(no `people/" + entityKey + "/` folder yet)\n";

        var files = Directory
            .EnumerateFiles(peopleDir, "*.md", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(MaxMarkdownFilesPerEntity)
            .ToList();

        if (files.Count == 0)
            return "(no `.md` files in this folder yet)\n";

        var sb = new StringBuilder();
        foreach (var full in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PersonaScenarioScope.IsUnderProjectRoot(projectRootFull, full))
                continue;

            var rel = Path.GetRelativePath(entityWorkspace, full).Replace('\\', '/');
            if (!rel.StartsWith("people/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!ProjectMemoryAccessGuard.CanRead(querySpec, rel))
                continue;

            var text = await ops.ReadDocumentAsync(querySpec, projectRootFull, entityWorkspace, rel, cancellationToken)
                .ConfigureAwait(false);
            sb.Append("#### ").AppendLine(rel);
            sb.AppendLine(string.IsNullOrEmpty(text) ? "(empty file)" : text.TrimEnd());
            sb.AppendLine();
        }

        return sb.Length == 0 ? "(no readable markdown files)\n" : sb.ToString();
    }
}
