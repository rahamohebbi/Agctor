using System.Text;
using System.Text.Json;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.ProjectMemory.Models;
using AgctorSDK.Core.ProjectMemory.Orchestration;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>Builds a routing prompt (user message + persona blurbs), calls Ollama, parses JSON per PRD-014 router response schema.</summary>
public sealed class ScenarioFlowRouterLlmService : IScenarioFlowRouterLlmService
{
    private readonly IProjectLoader _loader;
    private readonly IProjectMemoryLlmClient _llm;
    private readonly ILogger<ScenarioFlowRouterLlmService> _logger;

    public ScenarioFlowRouterLlmService(
        IProjectLoader loader,
        IProjectMemoryLlmClient llm,
        ILogger<ScenarioFlowRouterLlmService> logger)
    {
        _loader = loader;
        _llm = llm;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScenarioFlowRouterLlmResult> RouteAsync(
        string projectRoot,
        string userMessage,
        IReadOnlyList<ScenarioFlowRouterPersonaCandidate> candidates,
        ScenarioFlowRouterConfig config,
        CancellationToken cancellationToken = default,
        string? routingContext = null)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return ScenarioFlowRouterLlmResult.Fail("Project root is required for LLM routing.");
        if (candidates.Count == 0)
            return ScenarioFlowRouterLlmResult.Fail("No LlmNode candidates from Router.");

        LoadedProjectContext ctx;
        try
        {
            ctx = await _loader.LoadAsync(projectRoot.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Router LLM: failed to load project at {Root}", projectRoot);
            return ScenarioFlowRouterLlmResult.Fail(ex.Message);
        }

        var allowed = new HashSet<string>(candidates.Select(c => c.PersonaId), StringComparer.OrdinalIgnoreCase);
        var yamlLines = new StringBuilder();
        foreach (var c in candidates)
        {
            var spec = ctx.AgentSpecs.FirstOrDefault(a =>
                string.Equals(a.Id, c.PersonaId, StringComparison.OrdinalIgnoreCase));
            var blurb = BuildBlurb(spec);
            yamlLines.Append("- personaId: ").Append(c.PersonaId).Append('\n');
            yamlLines.Append("  graphNodeId: ").Append(c.NodeId).Append('\n');
            yamlLines.Append("  graphEdgeId: ").Append(c.EdgeId).Append('\n');
            if (!string.IsNullOrWhiteSpace(c.Label))
                yamlLines.Append("  label: ").Append(EscapeYamlScalar(c.Label!)).Append('\n');
            yamlLines.Append("  summary: ").Append(EscapeYamlScalar(blurb)).Append('\n');
            if (!string.IsNullOrWhiteSpace(c.LlmRoutingHint))
                yamlLines.Append("  routingHint: ").Append(EscapeYamlScalar(c.LlmRoutingHint!)).Append('\n');
        }

        var routerText = string.IsNullOrEmpty(routingContext) ? userMessage : routingContext;
        var prompt = BuildRoutingPrompt(routerText, yamlLines.ToString(), allowed, config);
        string raw;
        try
        {
            raw = await _llm.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Router LLM: Ollama call failed");
            return ScenarioFlowRouterLlmResult.Fail(ex.Message);
        }

        return ScenarioFlowRouterLlmParser.Parse(raw, allowed, config);
    }

    private static string BuildBlurb(AgentDefinitionSpec? spec)
    {
        if (spec == null)
            return "(agent spec not found in project memory)";
        var firstInstr = (spec.Instructions ?? new List<string>()).FirstOrDefault(i => !string.IsNullOrWhiteSpace(i));
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(spec.Name)) sb.Append(spec.Name.Trim()).Append(". ");
        if (!string.IsNullOrWhiteSpace(spec.Role)) sb.Append(spec.Role.Trim()).Append(". ");
        if (!string.IsNullOrWhiteSpace(spec.Description)) sb.Append(spec.Description.Trim()).Append(". ");
        if (!string.IsNullOrWhiteSpace(firstInstr))
            sb.Append(firstInstr.Trim());
        var s = sb.ToString().Trim();
        return s.Length > 0 ? s : spec.Id;
    }

    private static string EscapeYamlScalar(string s)
    {
        var t = s.Replace('\n', ' ').Trim();
        if (t.Length > 400)
            t = t[..400] + "…";
        return JsonSerializer.Serialize(t);
    }

    private static string BuildRoutingPrompt(
        string routingText,
        string candidatesYaml,
        HashSet<string> allowed,
        ScenarioFlowRouterConfig config)
    {
        var allowList = string.Join(", ", allowed.OrderBy(x => x, StringComparer.Ordinal));
        var sb = new StringBuilder();
        sb.AppendLine("You are a routing controller. Pick which persona agent(s) should handle the user's message.");
        sb.AppendLine();
        sb.AppendLine("Return ONE JSON object only (no markdown fences). Schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"schemaVersion\": \"1.0\",");
        sb.AppendLine("  \"targets\": [ { \"personaId\": \"<id>\", \"confidence\": 0.0-1.0, \"reason\": \"optional\" } ],");
        sb.AppendLine("  \"needsClarification\": false,");
        sb.AppendLine("  \"clarificationPrompt\": null");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- schemaVersion must be \"1.0\".");
        sb.Append("- targets[].personaId must be from this set only: ").AppendLine(allowList);
        if (config.TargetPolicy == ScenarioFlowRouterTargetPolicy.SingleBest)
        {
            sb.AppendLine("- Return exactly ONE target: the single best-matching personaId for this message.");
            sb.AppendLine("- Rank by fit to each candidate's routingHint and summary; set confidence for that choice only (other branches should not appear in targets).");
        }
        else
        {
            sb.AppendLine("- You may return multiple targets when more than one branch clearly applies; keep the list short.");
            if (config.MaxTargets is { } cap && cap > 0)
                sb.Append("- Return at most ").Append(cap).AppendLine(" target(s).");
        }

        sb.AppendLine("- If the message is ambiguous, set needsClarification true and put a short question in clarificationPrompt.");
        sb.AppendLine("- Each candidate may include routingHint — treat it as authoritative routing guidance for that branch.");
        sb.AppendLine();
        sb.AppendLine("Candidates (YAML):");
        sb.AppendLine(candidatesYaml);
        sb.AppendLine();
        sb.AppendLine("Routing context (user message or upstream pipeline text):");
        sb.Append(routingText);
        if (!string.IsNullOrWhiteSpace(config.LlmRoutingInstructions))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Global routing policy (from flow designer; apply together with routingHint lines):");
            sb.Append(config.LlmRoutingInstructions.Trim());
        }

        return sb.ToString();
    }

}
