using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using Microsoft.Extensions.Logging;

namespace AgctorSDK.Core.ProjectMemory.Coref;

/// <summary>
/// LLM-based coreference resolver. Multi-language by design: the prompt does not enumerate pronouns
/// in any language; instead the model is asked to detect implicit references and replace them with a
/// canonical slug from a constrained whitelist. Includes gating + LRU cache + slug validation.
/// </summary>
public sealed class LlmConversationCoreferenceResolver : IConversationCoreferenceResolver
{
    private const int MaxCharsForLlm = 220;
    private const int MaxPriorTurnsIncluded = 4;
    private const int CacheCapacity = 256;

    private readonly IProjectMemoryLlmClient _llm;
    private readonly ILogger<LlmConversationCoreferenceResolver>? _logger;
    private readonly ConcurrentDictionary<string, CoreferenceResolution> _cache = new();
    private readonly ConcurrentQueue<string> _cacheOrder = new();

    public LlmConversationCoreferenceResolver(
        IProjectMemoryLlmClient llm,
        ILogger<LlmConversationCoreferenceResolver>? logger = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _logger = logger;
    }

    public async Task<CoreferenceResolution> ResolveAsync(CoreferenceRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UserMessage))
            return CoreferenceResolution.Unchanged(request?.UserMessage ?? "", request?.CurrentFocus?.EntityKey, "empty-input");

        var current = request.CurrentFocus?.EntityKey;
        // Long messages are extractor's job; coref rewrite would risk corrupting facts.
        if (request.UserMessage.Length > MaxCharsForLlm)
            return CoreferenceResolution.Unchanged(request.UserMessage, current, "input-too-long");

        // Without prior context AND no focus, there is nothing to resolve to; skip the LLM call entirely.
        if (string.IsNullOrWhiteSpace(request.ConversationPrefix) && request.CurrentFocus == null)
            return CoreferenceResolution.Unchanged(request.UserMessage, null, "no-context");

        // Pre-check: if the message already explicitly names a known entity (canonical or alias),
        // skip the LLM since the extractor will pick it up directly, and pin activeSubject to that
        // explicit person so stale focus cannot leak into this turn.
        var knownAsArr = request.KnownEntities ?? Array.Empty<KnownEntity>();
        var explicitSubject = TryMatchExplicitKnownEntity(request.UserMessage, knownAsArr);
        if (!string.IsNullOrWhiteSpace(explicitSubject))
            return CoreferenceResolution.Unchanged(request.UserMessage, explicitSubject, "explicit-name-detected");

        var allowed = knownAsArr
            .Where(k => !string.IsNullOrWhiteSpace(k.EntityKey) && !IsPronounLike(k.EntityKey))
            .ToList();
        if (allowed.Count == 0)
            return CoreferenceResolution.Unchanged(request.UserMessage, current, "no-known-entities");

        var cacheKey = ComputeCacheKey(request, allowed);
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return new CoreferenceResolution
            {
                Changed = cached.Changed,
                RewrittenMessage = cached.RewrittenMessage,
                ActiveSubjectEntityKey = cached.ActiveSubjectEntityKey,
                Reason = cached.Reason + ";cache-hit"
            };
        }

        var prompt = BuildPrompt(request, allowed);
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
            _logger?.LogDebug(ex, "Coreference LLM call failed; returning unchanged.");
            return CoreferenceResolution.Unchanged(request.UserMessage, current, "llm-error");
        }

        var resolution = ParseResponse(raw, request.UserMessage, current, allowed);
        Cache(cacheKey, resolution);
        return resolution;
    }

    private static string? TryMatchExplicitKnownEntity(string message, IEnumerable<KnownEntity> entities)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        foreach (var e in entities)
        {
            if (IsPronounLike(e.EntityKey)) continue;
            if (ContainsWordCaseInsensitive(message, e.EntityKey)) return e.EntityKey;
            if (!IsPronounLike(e.DisplayName) && ContainsWordCaseInsensitive(message, e.DisplayName)) return e.EntityKey;
            if (e.Aliases == null) continue;
            foreach (var a in e.Aliases)
                if (!IsPronounLike(a) && ContainsWordCaseInsensitive(message, a)) return e.EntityKey;
        }
        return null;
    }

    private static bool ContainsWordCaseInsensitive(string haystack, string? needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) return false;
        return haystack.Contains(needle.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPronounLike(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var t = token.Trim().ToLowerInvariant();
        return t is "he" or "him" or "his" or "she" or "her" or "hers" or "they" or "them" or "their" or "theirs";
    }

    private static string BuildPrompt(CoreferenceRequest request, IReadOnlyList<KnownEntity> allowed)
    {
        var sb = new StringBuilder(700);
        sb.AppendLine("You normalize a short user reply by resolving implicit references (pronouns in any language,");
        sb.AppendLine("bare predicates without a subject) to a canonical entity slug.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Allowed slugs are ONLY the ones listed below. Never invent another slug.");
        sb.AppendLine("- If the message already explicitly names a person, return changed=false.");
        sb.AppendLine("- If the message has no implicit reference, return changed=false.");
        sb.AppendLine("- If the message refers to a previously named person without naming them, set changed=true,");
        sb.AppendLine("  rewrittenMessage with the canonical name in place of the pronoun (preserve the original language for the rest).");
        sb.AppendLine("- Always set activeSubject to the slug of the person the new message is about (or the most recent if unchanged).");
        sb.AppendLine("- Output JSON only. No prose.");
        sb.AppendLine();
        sb.AppendLine("Allowed entities:");
        foreach (var k in allowed)
        {
            sb.Append("- slug=").Append(k.EntityKey)
                .Append(", displayName=").Append(string.IsNullOrWhiteSpace(k.DisplayName) ? k.EntityKey : k.DisplayName);
            if (k.Aliases is { Count: > 0 })
                sb.Append(", aliases=[").Append(string.Join(", ", k.Aliases)).Append(']');
            sb.AppendLine();
        }

        if (request.CurrentFocus != null && !string.IsNullOrWhiteSpace(request.CurrentFocus.EntityKey))
        {
            sb.Append("Current active subject (most recently named): ")
                .Append(request.CurrentFocus.EntityKey);
            if (!string.IsNullOrWhiteSpace(request.CurrentFocus.DisplayName))
                sb.Append(" (").Append(request.CurrentFocus.DisplayName).Append(')');
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.ConversationPrefix))
        {
            sb.AppendLine();
            sb.AppendLine("Prior conversation (last few turns):");
            sb.AppendLine(TrimToLastTurns(request.ConversationPrefix!, MaxPriorTurnsIncluded));
        }

        sb.AppendLine();
        sb.Append("New user message:\n\"").Append(request.UserMessage).AppendLine("\"");
        sb.AppendLine();
        sb.AppendLine("Respond with JSON of this exact shape:");
        sb.AppendLine("{\"changed\":<bool>,\"rewrittenMessage\":\"<string>\",\"activeSubject\":\"<slug>\"}");
        return sb.ToString();
    }

    private static string TrimToLastTurns(string prefix, int maxTurns)
    {
        var lines = prefix.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= maxTurns) return string.Join('\n', lines);
        return string.Join('\n', lines.AsEnumerable().TakeLast(maxTurns));
    }

    private static CoreferenceResolution ParseResponse(string raw, string original, string? currentFocus, IReadOnlyList<KnownEntity> allowed)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return CoreferenceResolution.Unchanged(original, currentFocus, "empty-llm-response");

        var jsonText = ExtractJsonObject(raw);
        if (string.IsNullOrEmpty(jsonText))
            return CoreferenceResolution.Unchanged(original, currentFocus, "no-json-in-response");

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;
            var changed = root.TryGetProperty("changed", out var c) && c.ValueKind == JsonValueKind.True;
            var rewritten = root.TryGetProperty("rewrittenMessage", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString() ?? ""
                : "";
            var subjectRaw = root.TryGetProperty("activeSubject", out var s) && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;

            // If the model named an active subject not in the whitelist, treat the whole response as unsafe:
            // do not rewrite and fall back to the prior focus. This catches "the model invented a brand-new slug".
            string? validatedSubject = ValidateSlug(subjectRaw, allowed);
            if (!string.IsNullOrWhiteSpace(subjectRaw) && validatedSubject == null)
                return CoreferenceResolution.Unchanged(original, currentFocus, "active-subject-not-in-whitelist");

            var subject = validatedSubject ?? currentFocus;

            // Drop rewrites that leak machine slugs outside the whitelist (e.g. "person_1").
            if (changed && !string.IsNullOrWhiteSpace(rewritten))
            {
                if (!RewrittenIsConsistent(rewritten, allowed))
                    return CoreferenceResolution.Unchanged(original, subject, "rewrite-references-unknown-slug");

                return new CoreferenceResolution
                {
                    Changed = true,
                    RewrittenMessage = rewritten.Trim(),
                    ActiveSubjectEntityKey = subject,
                    Reason = "llm-rewrite"
                };
            }

            return CoreferenceResolution.Unchanged(original, subject, "llm-no-change");
        }
        catch (Exception)
        {
            return CoreferenceResolution.Unchanged(original, currentFocus, "json-parse-error");
        }
    }

    private static string? ValidateSlug(string? candidate, IReadOnlyList<KnownEntity> allowed)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        var normalized = candidate.Trim();
        foreach (var k in allowed)
            if (string.Equals(k.EntityKey, normalized, StringComparison.OrdinalIgnoreCase))
                return k.EntityKey;
        return null;
    }

    /// <summary>
    /// Rejects rewrites that leak an obvious machine slug (e.g. <c>person_1</c>) outside the whitelist.
    /// Common natural-language words (lowercase) are NOT treated as slugs; only tokens with characteristic
    /// slug shape (digit, underscore, or clearly artificial pattern) trigger the consistency check.
    /// </summary>
    private static bool RewrittenIsConsistent(string rewritten, IReadOnlyList<KnownEntity> allowed)
    {
        if (string.IsNullOrWhiteSpace(rewritten)) return false;
        foreach (var token in rewritten.Split(new[] { ' ', '\n', '\r', '\t', '"', '\'', ',', '.', ';', ':' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 3) continue;
            if (!HasSlugShape(token)) continue;
            var matched = false;
            foreach (var k in allowed)
            {
                if (string.Equals(k.EntityKey, token, StringComparison.OrdinalIgnoreCase)) { matched = true; break; }
            }
            if (!matched) return false;
        }
        return true;
    }

    /// <summary>True only for clearly machine-shaped slugs: contain an underscore or a digit, lowercase letters/digits/underscore only.</summary>
    private static bool HasSlugShape(string token)
    {
        var hasUnderscore = false;
        var hasDigit = false;
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            if (char.IsUpper(c)) return false;
            if (!(char.IsLower(c) || char.IsDigit(c) || c == '_'))
                return false;
            if (c == '_') hasUnderscore = true;
            else if (char.IsDigit(c)) hasDigit = true;
        }

        return hasUnderscore || hasDigit;
    }

    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return "";
        return raw.Substring(start, end - start + 1);
    }

    private static string ComputeCacheKey(CoreferenceRequest request, IReadOnlyList<KnownEntity> allowed)
    {
        var sb = new StringBuilder(256);
        sb.Append(request.UserMessage).Append('|');
        sb.Append(request.CurrentFocus?.EntityKey ?? "").Append('|');
        sb.Append(request.ConversationPrefix ?? "").Append('|');
        foreach (var k in allowed.OrderBy(k => k.EntityKey, StringComparer.Ordinal))
            sb.Append(k.EntityKey).Append(',');

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private void Cache(string key, CoreferenceResolution resolution)
    {
        if (!_cache.TryAdd(key, resolution)) return;
        _cacheOrder.Enqueue(key);
        while (_cache.Count > CacheCapacity && _cacheOrder.TryDequeue(out var oldest))
            _cache.TryRemove(oldest, out _);
    }
}
