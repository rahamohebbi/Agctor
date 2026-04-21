using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// S4 attribute overlap: compare tokens found in the candidate entity's markdown files against
/// tokens present in the session asserted facts. Score = Jaccard of lowercased, non-stopword
/// tokens (length &gt;= 4 to reduce noise). Neutral when either side has no content.
/// </summary>
/// <remarks>
/// This is a deliberately cheap baseline: future iterations can parse typed fields (birthday,
/// workplace) and use weighted similarity. The idempotency fingerprint hashes the joined token
/// sets so a re-read with identical content does not duplicate the signal.
/// </remarks>
public sealed class AttributeOverlap : ISignalProducer
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "the","and","with","for","from","that","this","they","them","their","there",
        "were","been","have","has","does","did","will","would","could","should",
        "about","into","than","then","when","where","which","while","because"
    };

    private readonly IReadOnlyList<string> _docFiles;

    public AttributeOverlap(IReadOnlyList<string>? docFiles = null)
    {
        _docFiles = docFiles ?? new[] { "profile.md", "timeline.md", "skills.md" };
    }

    public string Name => "AttributeOverlap@1";
    public string Kind => "attrOverlap";

    public ResolutionSignal? Score(SignalContext ctx, ResolutionPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(ctx.CandidateEntityPath))
            return null;
        if (ctx.SessionAssertedFacts == null || ctx.SessionAssertedFacts.Count == 0)
            return null;

        var candidateTokens = TokensFromFolder(ctx.CandidateEntityPath, _docFiles);
        if (candidateTokens.Count == 0) return null;

        var factTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fact in ctx.SessionAssertedFacts)
            foreach (var t in Tokenize(fact))
                factTokens.Add(t);

        if (factTokens.Count == 0) return null;

        var intersect = 0;
        foreach (var t in factTokens) if (candidateTokens.Contains(t)) intersect++;
        var union = candidateTokens.Count + factTokens.Count - intersect;
        if (union <= 0) return null;

        double score = (double)intersect / union;
        if (score <= 0) return null;

        return new ResolutionSignal
        {
            Kind = Kind,
            ProducedBy = Name,
            Score = score,
            Weight = policy.WeightFor(Kind),
            Rationale = $"Jaccard token overlap = {score:F2} ({intersect}/{union})",
            InputsFingerprint = FingerprintUtil.Of(
                Kind,
                ctx.CandidateEntityKey,
                candidateTokens.Count.ToString(),
                factTokens.Count.ToString(),
                intersect.ToString())
        };
    }

    private static HashSet<string> TokensFromFolder(string folder, IReadOnlyList<string> docFiles)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rel in docFiles)
        {
            var path = Path.Combine(folder, rel);
            if (!File.Exists(path)) continue;
            var text = File.ReadAllText(path);
            foreach (var t in Tokenize(text)) tokens.Add(t);
        }
        return tokens;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0)
            {
                var tok = sb.ToString();
                if (tok.Length >= 4 && !Stopwords.Contains(tok)) yield return tok;
                sb.Clear();
            }
        }
        if (sb.Length >= 4)
        {
            var tok = sb.ToString();
            if (!Stopwords.Contains(tok)) yield return tok;
        }
    }
}
