using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Orchestration;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>
/// Builds JSON blobs stored on trace activities for the Trace timeline drill-down (playground-only shapes).
/// </summary>
internal static class PlaygroundTraceTimelineDetail
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public const int MaxPromptChars = 96 * 1024;
    public const int MaxOutputChars = 96 * 1024;
    public const int MaxFilePreviewChars = 12 * 1024;
    public const int MaxPersistFileEntries = 24;

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

    public static string BuildIngestJson(string scenarioId, ProjectMemoryIngestResult ingest)
    {
        var paths = ingest.UpdatedFiles.Take(40).ToArray();
        return JsonSerializer.Serialize(
            new
            {
                kind = "pm.playground.ingest-disk",
                scenarioId,
                ingest.ParseSuccess,
                ingest.WroteAnyFile,
                ingest.Summary,
                paths,
                pathsTruncated = ingest.UpdatedFiles.Count > paths.Length
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
