using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Resolution.Trace;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Builds JSON blobs stored on trace activities for the Trace timeline drill-down (playground-only shapes).
/// </summary>
internal static class PlaygroundTraceTimelineDetail
{
    /// <summary>
    /// Build the <c>timelineDetailJson</c> payload for a <c>pm.playground.resolve</c> span (PRD-018).
    /// Exposes Input / Evidence / Outcome so the trace timeline renderer can reuse the same
    /// drill-down card layout as the other playground spans.
    /// </summary>
    public static string BuildResolveJson(ResolveSpanDetail detail)
    {
        return JsonSerializer.Serialize(new
        {
            kind = "pm.playground.resolve",
            input = detail.Input,
            evidence = detail.Evidence,
            outcome = detail.Outcome
        }, Json);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public const int MaxPromptChars = 96 * 1024;
    public const int MaxOutputChars = 96 * 1024;
    public const int MaxFilePreviewChars = 12 * 1024;
    /// <summary>Caps raw extractor text embedded on the ingest span so traces stay lightweight.</summary>
    public const int MaxIngestExtractorPreviewChars = 8 * 1024;
    public const int MaxPersistFileEntries = 24;
    public const int MaxRootPersonaEntries = 12;

    public static string BuildPersonaLlmJson(string prompt, string output, string model, string ollamaBase)
    {
        var pt = Truncate(prompt, MaxPromptChars);
        var ot = Truncate(output, MaxOutputChars);
        return JsonSerializer.Serialize(
            new
            {
                kind = "pm.playground.persona-llm",
                model,
                ollamaBase,
                promptChars = prompt.Length,
                outputChars = output.Length,
                prompt = pt,
                output = ot,
                promptTruncated = prompt.Length > pt.Length,
                outputTruncated = output.Length > ot.Length
            },
            Json);
    }

    /// <param name="extractorOutput">Raw LLM output fed into ingest (person-extractor stream); truncated in JSON for the trace UI.</param>
    public static string BuildIngestJson(string scenarioId, ProjectMemoryIngestResult ingest, string? extractorOutput = null)
    {
        var paths = ingest.UpdatedFiles.Take(40).ToArray();
        var preview = "";
        var extractorChars = 0;
        var extractorTruncated = false;
        if (!string.IsNullOrEmpty(extractorOutput))
        {
            extractorChars = extractorOutput.Length;
            preview = Truncate(extractorOutput, MaxIngestExtractorPreviewChars);
            extractorTruncated = extractorOutput.Length > preview.Length;
        }

        var oos = ingest.OutOfSchemaProposals?.Take(20)
            .Select(p => new
            {
                p.ProposalId,
                disposition = p.Disposition.ToString(),
                p.UserPromptLine
            })
            .ToArray();

        return JsonSerializer.Serialize(
            new
            {
                kind = "pm.playground.ingest-disk",
                scenarioId,
                ingest.ParseSuccess,
                ingest.WroteAnyFile,
                ingest.ParseSource,
                ingest.Summary,
                paths,
                pathsTruncated = ingest.UpdatedFiles.Count > paths.Length,
                extractorOutputChars = extractorChars > 0 ? extractorChars : (int?)null,
                extractorOutputPreview = string.IsNullOrEmpty(preview) ? null : preview,
                extractorOutputTruncated = extractorTruncated,
                outOfSchemaProposals = oos is { Length: > 0 } ? oos : null,
                outOfSchemaTruncated = (ingest.OutOfSchemaProposals?.Count ?? 0) > 20
            },
            Json);
    }

    public static string BuildPersistJson(
        string sessionId,
        string messageId,
        int assistantChars,
        ProjectMemoryIngestResult? ingest,
        string projectRootFull)
    {
        var fileEntries = new List<object>();
        if (ingest?.UpdatedFiles is { Count: > 0 })
        {
            foreach (var rel in ingest.UpdatedFiles.Take(MaxPersistFileEntries))
            {
                var full = ToAbsolutePath(rel, projectRootFull);
                long fileSizeBytes = 0;
                var previewChars = 0;
                string preview = "";
                var truncated = false;
                var readError = (string?)null;
                try
                {
                    if (File.Exists(full))
                    {
                        var fi = new FileInfo(full);
                        fileSizeBytes = fi.Length;
                        using var sr = new StreamReader(full);
                        var buf = new char[MaxFilePreviewChars];
                        var n = sr.ReadBlock(buf, 0, buf.Length);
                        previewChars = n;
                        preview = new string(buf, 0, n);
                        truncated = sr.Read() >= 0;
                    }
                    else
                    {
                        readError = "File not found on disk at resolved path.";
                    }
                }
                catch (Exception ex)
                {
                    readError = ex.Message;
                }

                fileEntries.Add(new
                {
                    path = rel,
                    resolvedPath = full,
                    fileSizeBytes,
                    previewChars,
                    preview,
                    previewTruncated = truncated,
                    readError
                });
            }
        }

        return JsonSerializer.Serialize(
            new
            {
                kind = "pm.playground.persist-assistant",
                sessionId,
                messageId,
                assistantChars,
                ingestSummary = ingest?.Summary,
                files = fileEntries
            },
            Json);
    }

    public static string BuildStreamRootJson(
        string sessionId,
        string messageId,
        string? scenarioId,
        string selectedAgentId,
        bool usedScenarioFlow,
        string status,
        string? errorMessage = null,
        IEnumerable<string>? personaChain = null,
        int? responseChars = null,
        bool? ingestAttempted = null)
    {
        var chain = (personaChain ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Take(MaxRootPersonaEntries)
            .ToArray();

        return JsonSerializer.Serialize(
            new
            {
                kind = "pm.playground.stream-root",
                sessionId,
                messageId,
                scenarioId,
                selectedAgentId,
                usedScenarioFlow,
                status,
                errorMessage,
                responseChars,
                ingestAttempted,
                personaChain = chain.Length > 0 ? chain : null,
                personaChainTruncated = (personaChain?.Count() ?? 0) > chain.Length
            },
            Json);
    }

    private static string ToAbsolutePath(string path, string projectRootFull)
    {
        if (string.IsNullOrWhiteSpace(path))
            return projectRootFull;
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        var trimmed = path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(projectRootFull, trimmed));
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s.Substring(0, max);
    }
}
