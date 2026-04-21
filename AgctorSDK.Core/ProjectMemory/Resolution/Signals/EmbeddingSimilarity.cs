using System;
using System.IO;
using System.Linq;
using System.Text;
using AgctorSDK.Core.ProjectMemory.Resolution.Models;

namespace AgctorSDK.Core.ProjectMemory.Resolution.Signals;

/// <summary>
/// S5 embedding similarity: cosine of embedding(session facts) vs embedding(candidate profile).
/// Falls back to null when <see cref="IEmbeddingProvider.IsAvailable"/> is false or either side
/// produces no embedding; this keeps the signal always-optional per PRD-018.
/// </summary>
/// <remarks>
/// Runs the provider synchronously inside <see cref="Score"/> via GetAwaiter().GetResult() to fit
/// the ISignalProducer contract. If your provider is slow, wire it behind a cache or move scoring
/// to a pre-pass in the reconciler.
/// </remarks>
public sealed class EmbeddingSimilarity : ISignalProducer
{
    private readonly IEmbeddingProvider _provider;

    public EmbeddingSimilarity(IEmbeddingProvider provider)
    {
        _provider = provider ?? new NullEmbeddingProvider();
    }

    public string Name => "EmbeddingSimilarity@1";
    public string Kind => "embedding";

    public ResolutionSignal? Score(SignalContext ctx, ResolutionPolicy policy)
    {
        if (!_provider.IsAvailable) return null;
        if (ctx.SessionAssertedFacts == null || ctx.SessionAssertedFacts.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(ctx.CandidateEntityPath)) return null;

        var candidateText = ReadCandidateText(ctx.CandidateEntityPath);
        if (string.IsNullOrWhiteSpace(candidateText)) return null;

        var factsText = string.Join("\n", ctx.SessionAssertedFacts);

        float[]? a;
        float[]? b;
        try
        {
            a = _provider.EmbedAsync(factsText).GetAwaiter().GetResult();
            b = _provider.EmbedAsync(candidateText).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }

        if (a == null || b == null || a.Length == 0 || a.Length != b.Length) return null;

        double cos = Cosine(a, b);
        if (cos <= 0) return null;

        return new ResolutionSignal
        {
            Kind = Kind,
            ProducedBy = Name,
            Score = Math.Min(1.0, cos),
            Weight = policy.WeightFor(Kind),
            Rationale = $"Cosine similarity = {cos:F2} over {a.Length}-dim embeddings",
            InputsFingerprint = FingerprintUtil.Of(Kind, ctx.CandidateEntityKey, a.Length.ToString(), HashOf(factsText), HashOf(candidateText))
        };
    }

    private static string ReadCandidateText(string folder)
    {
        var sb = new StringBuilder();
        foreach (var rel in new[] { "profile.md", "timeline.md", "skills.md" })
        {
            var p = Path.Combine(folder, rel);
            if (File.Exists(p))
            {
                sb.AppendLine(File.ReadAllText(p));
            }
        }
        return sb.ToString();
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        if (na == 0 || nb == 0) return 0;
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private static string HashOf(string text)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToBase64String(bytes).Substring(0, 12);
    }
}
