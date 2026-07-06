using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Rag;
using AgctorSDK.Core.Rag;

namespace AgctorSDK.Core.ProjectMemory.Tools;

/// <summary>Optional RAG overrides from LlmNode.config or PersonMemoryContextTool parameters (PRD-025).</summary>
public sealed record PersonMemoryRagOptions(
    string? ProviderId = null,
    string? CollectionId = null,
    int TopK = 8);

/// <summary>
/// Loads scenario-scoped people markdown for person-query style prompts. Shared by the HTTP playground,
/// the PersonMemoryContextTool actor, and tests.
/// </summary>
public static class PersonMemoryMarkdownContextBuilder
{
    public const int MaxEntities = 20;
    public const int MaxMarkdownFilesPerEntity = 40;
    public const int MaxAppendixChars = 120_000;

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

    /// <summary>Reads optional ragProviderId / ragCollectionId / ragTopK from flow node config.</summary>
    public static PersonMemoryRagOptions ParseRagOptions(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } el)
            return new PersonMemoryRagOptions();

        string? providerId = null;
        if (el.TryGetProperty("ragProviderId", out var p))
            providerId = p.GetString()?.Trim();

        string? collectionId = null;
        if (el.TryGetProperty("ragCollectionId", out var c))
            collectionId = c.GetString()?.Trim();

        var topK = 8;
        if (el.TryGetProperty("ragTopK", out var k))
        {
            if (k.ValueKind == JsonValueKind.Number && k.TryGetInt32(out var n))
                topK = Math.Clamp(n, 1, 100);
            else if (k.ValueKind == JsonValueKind.String && int.TryParse(k.GetString(), out n))
                topK = Math.Clamp(n, 1, 100);
        }

        return new PersonMemoryRagOptions(providerId, collectionId, topK);
    }

    /// <summary>Builds rag options from PersonMemoryContextTool parameter dictionary.</summary>
    public static PersonMemoryRagOptions ParseRagOptionsFromParameters(
        string? providerId,
        string? collectionId,
        int? topK) =>
        new(
            string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim(),
            string.IsNullOrWhiteSpace(collectionId) ? null : collectionId.Trim(),
            topK is > 0 ? Math.Clamp(topK.Value, 1, 100) : 8);

    /// <summary>Best-effort token for <see cref="ProjectMemoryOperations.SearchEntitiesAsync"/> substring match.</summary>
    public static string? ExtractFocusQueryFromUserMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var t = message.Trim();
        var quoted = Regex.Match(t, """["“]([^"”]{2,60})["”]""");
        if (quoted.Success)
            return quoted.Groups[1].Value.Trim();

        var possessive = Regex.Match(t, @"\b([A-Za-z][a-zA-Z0-9]{1,40})['\u2019]s\b");
        if (possessive.Success)
            return possessive.Groups[1].Value.Trim();

        foreach (var prefix in new[]
                 {
                     "who is", "who's", "what is", "what's", "tell me about", "do you know",
                     "who was", "what was", "i am", "i'm"
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

    /// <summary>Builds an appendix block for the persona LLM prompt.</summary>
    /// <param name="loadedViaLine">When null, uses the legacy playground wording; tools pass an explicit provenance line.</param>
    public static async Task<string> BuildAppendixAsync(
        ProjectMemoryOperations ops,
        AgentDefinitionSpec querySpec,
        string projectRootFull,
        string? scenarioId,
        string strategy,
        string focusSourceUserMessage,
        CancellationToken cancellationToken,
        string? loadedViaLine = null,
        RagContextService? ragService = null,
        PersonMemoryRagOptions? ragOptions = null)
    {
        var notes = new StringBuilder();
        var effectiveStrategy = strategy;

        if (IsRagStrategy(strategy))
        {
            var ragResult = await TryBuildRagAppendixAsync(
                    ragService,
                    ragOptions,
                    strategy,
                    focusSourceUserMessage,
                    scenarioId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (ragResult?.UsedExternalRag == true && !string.IsNullOrWhiteSpace(ragResult.Appendix))
            {
                var sb = new StringBuilder();
                sb.AppendLine(ragResult.Appendix.TrimEnd());
                AppendPersonQueryInstructions(sb, querySpec, loadedViaLine
                    ?? $"External RAG context loaded via {ragResult.ProviderId} (read-only).",
                    externalRag: true);
                if (notes.Length > 0)
                    sb.AppendLine(notes.ToString().TrimEnd());
                return sb.ToString().TrimEnd();
            }

            var reason = ragResult?.FallbackReason ?? "RAG service not available";
            notes.AppendLine(
                $"Note: contextStrategy '{strategy}' could not use external RAG ({reason}); loaded on-disk markdown like markdown_focus.");
            effectiveStrategy = "markdown_focus";
        }

        var entityWorkspace = PersonaScenarioScope.GetEntityWorkspaceRoot(projectRootFull, scenarioId);
        if (!PersonaScenarioScope.IsUnderProjectRoot(projectRootFull, entityWorkspace))
        {
            return """
                   ---
                   Person-query context: entity workspace path is invalid; cannot load markdown.

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
            return "---\nPerson-query context: failed to list entities — " + ex.Message + "\n";
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
            head.AppendLine("Person-query context: no entities found under this workspace.");
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

        var markdownSb = new StringBuilder();
        markdownSb.AppendLine("---");
        markdownSb.AppendLine(loadedViaLine
                              ?? "Person-query: markdown below was read from disk (playground single-step path; no tool loop).");
        AppendPersonQueryInstructions(markdownSb, querySpec, headerAlreadyWritten: true);
        if (notes.Length > 0)
            markdownSb.AppendLine(notes.ToString().TrimEnd());
        markdownSb.AppendLine();
        markdownSb.AppendLine(combined);
        return markdownSb.ToString().TrimEnd();
    }

    private static bool IsRagStrategy(string strategy) =>
        string.Equals(strategy, "rag", StringComparison.OrdinalIgnoreCase)
        || string.Equals(strategy, "graph_rag", StringComparison.OrdinalIgnoreCase);

    private static async Task<RagContextAppendixResult?> TryBuildRagAppendixAsync(
        RagContextService? ragService,
        PersonMemoryRagOptions? ragOptions,
        string strategy,
        string userMessage,
        string? scenarioId,
        CancellationToken cancellationToken)
    {
        if (ragService == null)
            return RagContextAppendixResult.Fallback("RAG service not registered");

        var opts = ragOptions ?? new PersonMemoryRagOptions();
        var mode = string.Equals(strategy, "graph_rag", StringComparison.OrdinalIgnoreCase)
            ? RagQueryMode.Graph
            : RagQueryMode.Hybrid;
        var collectionId = string.IsNullOrWhiteSpace(opts.CollectionId) ? scenarioId : opts.CollectionId;

        return await ragService.BuildAppendixAsync(
            new RagContextRequest(
                userMessage,
                opts.ProviderId,
                collectionId,
                mode,
                opts.TopK,
                MaxAppendixChars),
            cancellationToken).ConfigureAwait(false);
    }

    private static void AppendPersonQueryInstructions(
        StringBuilder sb,
        AgentDefinitionSpec querySpec,
        string? loadedViaLine = null,
        bool headerAlreadyWritten = false,
        bool externalRag = false)
    {
        if (!headerAlreadyWritten && !string.IsNullOrWhiteSpace(loadedViaLine))
            sb.AppendLine(loadedViaLine);

        if (IsRelationshipCoachingSpec(querySpec))
        {
            sb.AppendLine(externalRag
                ? "Relationship coaching: use ONLY the retrieved context below. Map who is speaking from relationships when cited (e.g. user → child: ryan means the user is Ryan's parent)."
                : "Relationship coaching: use ONLY the markdown below. Map who is speaking from relationships.md (e.g. user → child: ryan means the user is Ryan's parent).");
            sb.AppendLine(
                "Give advice TO the user in their role toward the named person. Do NOT invent age, grade, hobbies, or timeline events absent from the files.");
            sb.AppendLine(
                "If profile does not state an age, say age is unknown — do not assume a teenager or adult.");
        }
        else
        {
            sb.AppendLine(externalRag
                ? "Use the retrieved context below together with factual statements in the latest user message."
                : "Use stored markdown below together with factual statements in the latest user message.");
            sb.AppendLine(
                "When the user states a new fact and asks a follow-up in the same turn, treat those stated facts as authoritative for this reply.");
            sb.AppendLine(
                "You may apply reasonable inference from stated facts (e.g. chalk → drawing on outdoor surfaces) when the question follows directly.");
            sb.AppendLine(externalRag
                ? "Answer in plain text; prefer retrieved context for historical facts when it does not conflict with this turn."
                : "Answer in plain text; prefer on-disk markdown for historical facts when it does not conflict with this turn.");
        }
    }

    private static bool IsRelationshipCoachingSpec(AgentDefinitionSpec spec) =>
        string.Equals(spec.Id, "relationship-coach", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spec.Role, "coaching", StringComparison.OrdinalIgnoreCase);

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
