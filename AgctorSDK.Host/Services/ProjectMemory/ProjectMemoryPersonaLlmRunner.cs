using System.Text;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;
using AgctorSDK.Core.ProjectMemory.Yaml;
using AgctorSDK.Core.Sessions.Models;
using AgctorSDK.Host.Services;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <inheritdoc />
public sealed class ProjectMemoryPersonaLlmRunner : IProjectMemoryPersonaLlmRunner
{
    private readonly IProjectLoader _loader;
    private readonly ISessionStore _sessions;
    private readonly IProjectMemoryPipelineRunner _pipeline;
    private readonly ILogger<ProjectMemoryPersonaLlmRunner> _logger;

    public ProjectMemoryPersonaLlmRunner(
        IProjectLoader loader,
        ISessionStore sessions,
        IProjectMemoryPipelineRunner pipeline,
        ILogger<ProjectMemoryPersonaLlmRunner> logger)
    {
        _loader = loader;
        _sessions = sessions;
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProjectMemoryPersonaRunResult> RunAsync(
        string projectRoot,
        string? sessionId,
        string agentId,
        string inputText,
        CancellationToken cancellationToken = default,
        string? scenarioId = null)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return new ProjectMemoryPersonaRunResult(false, "Project root is required.", null);
        if (string.IsNullOrWhiteSpace(agentId))
            return new ProjectMemoryPersonaRunResult(false, "agentId is required.", null);
        if (string.IsNullOrWhiteSpace(inputText))
            return new ProjectMemoryPersonaRunResult(false, "inputText is required.", null);

        LoadedProjectContext ctx;
        try
        {
            ctx = await _loader.LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persona runner: failed to load project at {Root}", projectRoot);
            return new ProjectMemoryPersonaRunResult(false, ex.Message, null);
        }

        var spec = ctx.AgentSpecs.FirstOrDefault(a =>
            string.Equals(a.Id, agentId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (spec == null)
            return new ProjectMemoryPersonaRunResult(false, $"Agent spec '{agentId}' not found in project memory.", null);

        IReadOnlyList<SessionTurn>? prior = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            try
            {
                prior = await _sessions.GetTurnsAsync(sessionId.Trim(), null, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Persona runner: could not load session {SessionId}", sessionId);
            }
        }

        var prompt = BuildPlaygroundPrompt(spec, prior, inputText, scenarioId);
        try
        {
            var output = await OllamaGenerateApi.GenerateNonStreamingAsync(prompt, cancellationToken).ConfigureAwait(false);

            // PRD-014 PersonaCall + playground: person-extractor emits JSON; when a scenario id is present, apply the same ingest as the deterministic pipeline.
            var scoped = !string.IsNullOrWhiteSpace(scenarioId)
                         && string.Equals(agentId.Trim(), "person-extractor", StringComparison.OrdinalIgnoreCase);
            if (!scoped)
                return new ProjectMemoryPersonaRunResult(true, null, output);

            var rootFull = Path.GetFullPath(projectRoot.Trim());
            var ingest = await _pipeline
                .IngestFromExtractorOutputAsync(rootFull, scenarioId!.Trim(), output, cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<string>? paths = ingest.UpdatedFiles.Count > 0 ? ingest.UpdatedFiles : null;
            var summary = ingest.Summary;
            if (ingest.WroteAnyFile && paths is { Count: > 0 })
                _logger.LogInformation("Persona runner: ingested {Count} file(s) under scenario {ScenarioId}", paths.Count, scenarioId);
            else if (!ingest.ParseSuccess)
                _logger.LogWarning(
                    "Persona runner: ingest parse failed for scenario {ScenarioId}: {Summary}. Output prefix: {Prefix}",
                    scenarioId,
                    summary ?? "",
                    TruncateForIngestLog(output));

            var combined = AppendIngestFooter(output, ingest);
            return new ProjectMemoryPersonaRunResult(true, null, combined, paths, summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persona runner: LLM call failed for {AgentId}", agentId);
            return new ProjectMemoryPersonaRunResult(false, ex.Message, null);
        }
    }

    /// <summary>Footer text after extractor JSON ingest (playground stream + persona runner).</summary>
    public static string FormatIngestFooter(ProjectMemoryIngestResult ingest)
    {
        if (!ingest.ParseSuccess)
            return "Ingest (parse): " + (ingest.Summary ?? "failed");
        if (!ingest.WroteAnyFile)
            return "Ingest: " + (ingest.Summary ?? "No files updated.");
        return "Written:\n" + string.Join("\n", ingest.UpdatedFiles);
    }

    /// <summary>Appends standard <c>---</c> ingest block to raw LLM output.</summary>
    public static string AppendIngestFooter(string extractorLlmOutput, ProjectMemoryIngestResult ingest) =>
        extractorLlmOutput + "\n\n---\n" + FormatIngestFooter(ingest);

    /// <summary>Optional appendix (e.g. ingest telemetry for memory-curator in scenario-flow playground).</summary>
    public static string BuildPlaygroundPrompt(
        AgentDefinitionSpec spec,
        IReadOnlyList<SessionTurn>? priorTurns,
        string newUserText,
        string? scenarioId = null,
        string? playgroundFlowAppendix = null)
    {
        var lines = (spec.Instructions ?? new List<string>()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        var specHeader = $"Agent: {spec.Id}\nRole: {spec.Role}\nName: {spec.Name}\n";
        var outputHint = spec.Output?.Type?.Contains("intent", StringComparison.OrdinalIgnoreCase) == true
            ? "Return valid JSON only. Do not wrap JSON in markdown fences."
            : "Respond in plain text unless JSON is explicitly required by the instructions.";

        var sb = new StringBuilder();
        sb.Append(string.Join('\n', lines));
        sb.Append("\n\n").Append(specHeader).Append(outputHint);
        if (!string.IsNullOrWhiteSpace(scenarioId))
        {
            var seg = PersonaScenarioScope.SanitizeFolderSegment(scenarioId);
            sb.Append("\n\nScenario-scoped persona data for this run lives under project-relative `scenarios/")
                .Append(seg)
                .Append("/people/` (not the project-root `people/` folder).");
        }

        if (priorTurns is { Count: > 0 })
        {
            sb.Append("\n\n---\nConversation so far:\n");
            foreach (var t in priorTurns.OrderBy(x => x.Sequence))
            {
                if (t.Role is SessionRole.System or SessionRole.Tool)
                    continue;
                var label = t.Role == SessionRole.User ? "User" : "Assistant";
                sb.Append(label).Append(": ").Append(t.Content).Append('\n');
            }
        }

        sb.Append("\n---\nLatest user message:\n").Append(newUserText);
        if (!string.IsNullOrWhiteSpace(playgroundFlowAppendix))
            sb.Append("\n\n").Append(playgroundFlowAppendix.Trim());
        return sb.ToString();
    }

    /// <summary>
    /// Curator must not hallucinate <c>write_document</c> results: playground only applies disk changes via ingest of extractor JSON.
    /// </summary>
    public static string BuildPlaygroundFlowIngestHint(ProjectMemoryIngestResult ingest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("Playground runtime: write_document and other tools are NOT executed in this HTTP flow.");
        sb.AppendLine(
            "Disk changes come only from parsing the upstream person-extractor memoryIntents JSON (ingest may already have run).");
        if (!ingest.ParseSuccess)
        {
            sb.Append("Ingest: PARSE FAILED — ").AppendLine(ingest.Summary ?? "");
            sb.AppendLine("Do not claim any markdown files were created or updated on disk.");
            sb.AppendLine(
                "Canonical extractor shape: a single JSON object with key \"memoryIntents\" (array). Root must be { ... }, not a bare [ ... ] array.");
            sb.AppendLine(
                "Example: {\"memoryIntents\":[{\"entityKey\":\"melody\",\"knowledgeType\":\"profile_fact\",\"attribute\":\"name\",\"value\":\"Melody\",\"confidence\":1}]}");
            sb.AppendLine("Use entityKey as a short folder slug (e.g. melody), not a match/ path.");
            return sb.ToString().TrimEnd();
        }

        if (ingest.WroteAnyFile && ingest.UpdatedFiles.Count > 0)
        {
            sb.AppendLine("Ingest: files were written (paths below). You may summarize these.");
            foreach (var p in ingest.UpdatedFiles.Take(25))
                sb.Append("- ").AppendLine(p);
            if (ingest.UpdatedFiles.Count > 25)
                sb.Append("(+").Append(ingest.UpdatedFiles.Count - 25).AppendLine(" more paths)");
        }
        else
        {
            sb.Append("Ingest: NO files written — ").AppendLine(ingest.Summary ?? "no detail");
            sb.AppendLine("Do not claim markdown files were created or updated unless you are quoting the list above.");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Short prefix for logs / traces when ingest JSON fails to parse.</summary>
    public static string TruncateForIngestLog(string? text, int max = 240)
    {
        var t = text?.Trim() ?? "";
        if (t.Length <= max)
            return t;
        return t[..max] + "…";
    }

}
