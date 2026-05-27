using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using Microsoft.Extensions.Logging;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>Light LLM call: primary conversation subject from message + whitelist (replaces lexical name matching).</summary>
public sealed class FocusSubjectResolver : IFocusSubjectResolver
{
    private const int MaxCharsForLlm = 400;
    private const int MaxPriorTurnsIncluded = 3;

    private readonly IProjectMemoryLlmClient _llm;
    private readonly ILogger<FocusSubjectResolver>? _logger;

    public FocusSubjectResolver(IProjectMemoryLlmClient llm, ILogger<FocusSubjectResolver>? logger = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger;
    }

    public async Task<FocusSubjectResult> ResolveAsync(
        FocusSubjectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserMessage))
            return FocusSubjectResult.Unchanged(request?.CurrentFocusEntityKey, "empty-input");

        var current = FocusEntityPolicy.NormalizeSlugOrNull(request.CurrentFocusEntityKey);
        if (request.UserMessage.Length > MaxCharsForLlm)
            return FocusSubjectResult.Unchanged(current, "input-too-long");

        var allowed = (request.KnownEntities ?? Array.Empty<KnownEntity>())
            .Where(k => !string.IsNullOrWhiteSpace(k.EntityKey) && !FocusEntityPolicy.IsPlaceholderSlug(k.EntityKey))
            .ToList();
        if (allowed.Count == 0)
            return FocusSubjectResult.Unchanged(current, "no-known-entities");

        var prompt = BuildPrompt(request, allowed, current);
        string raw;
        try
        {
            raw = await _llm.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "FocusSubject LLM call failed.");
            return FocusSubjectResult.Unchanged(current, "llm-error");
        }

        if (!TryParseResponse(raw, allowed, out var slug, out var reason))
            return FocusSubjectResult.Unchanged(current, "parse-failed");

        slug = FocusEntityPolicy.NormalizeSlugOrNull(slug);
        if (string.IsNullOrWhiteSpace(slug))
            return FocusSubjectResult.Unchanged(current, "invalid-slug");

        var display = LookupDisplay(slug, allowed) ?? slug;
        return new FocusSubjectResult
        {
            EntityKey = slug,
            DisplayName = display,
            Reason = reason ?? "focus-subject-llm",
            ChangedFromCurrent = !string.Equals(slug, current, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string BuildPrompt(
        FocusSubjectRequest request,
        IReadOnlyList<KnownEntity> allowed,
        string? currentFocus)
    {
        var sb = new StringBuilder(600);
        sb.AppendLine("Pick the primary person this user message is about.");
        sb.AppendLine("Output JSON only: {\"activeSubject\":\"slug\",\"reason\":\"brief\"}");
        sb.AppendLine("Rules:");
        sb.AppendLine("- activeSubject MUST be one of the allowed slugs below (never invent a new slug).");
        sb.AppendLine("- When several people are named, choose who the message is mainly about (grammatical subject),");
        sb.AppendLine("  not someone mentioned in passing or in a possessive phrase.");
        sb.AppendLine("- Example: \"Ryan is Raha's son\" → activeSubject is ryan, not raha.");
        sb.AppendLine("- If the message is only about the current focus person, return that slug.");
        sb.AppendLine();
        sb.AppendLine("Allowed slugs:");
        foreach (var k in allowed)
        {
            sb.Append("- ").Append(k.EntityKey);
            if (!string.IsNullOrWhiteSpace(k.DisplayName) && !string.Equals(k.DisplayName, k.EntityKey, StringComparison.OrdinalIgnoreCase))
                sb.Append(" (").Append(k.DisplayName).Append(')');
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(currentFocus))
            sb.AppendLine("Current focus slug: " + currentFocus);

        var prefix = TrimConversationPrefix(request.ConversationPrefix);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            sb.AppendLine();
            sb.AppendLine("Recent conversation:");
            sb.AppendLine(prefix);
        }

        sb.AppendLine();
        sb.AppendLine("User message:");
        sb.AppendLine(request.UserMessage.Trim());
        return sb.ToString();
    }

    private static bool TryParseResponse(
        string raw,
        IReadOnlyList<KnownEntity> allowed,
        out string? slug,
        out string? reason)
    {
        slug = null;
        reason = null;
        var json = MemoryIntentJson.UnwrapMarkdownFences(raw ?? "");
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "activeSubject", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String)
                    slug = prop.Value.GetString();
                if (string.Equals(prop.Name, "reason", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String)
                    reason = prop.Value.GetString();
            }
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(slug))
            return false;

        var normalized = FocusEntityPolicy.NormalizeSlugOrNull(slug);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return allowed.Any(a =>
            string.Equals(a.EntityKey, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TrimConversationPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return null;

        var lines = prefix.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length <= MaxPriorTurnsIncluded)
            return string.Join('\n', lines);
        return string.Join('\n', lines.Skip(Math.Max(0, lines.Length - MaxPriorTurnsIncluded)));
    }

    private static string? LookupDisplay(string slug, IReadOnlyList<KnownEntity> entities)
    {
        foreach (var e in entities)
            if (string.Equals(e.EntityKey, slug, StringComparison.OrdinalIgnoreCase))
                return e.DisplayName;
        return null;
    }
}
