using System.Text.Json;
using System.Text.Json.Nodes;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.Models;

public sealed class ModelSelectionService(ISessionStore sessions, IScenarioCatalog scenarios, IConfiguration configuration)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SessionModelSettings> SettingsAsync(string? sessionId, string? scenarioId, CancellationToken ct)
    {
        var json = string.IsNullOrWhiteSpace(sessionId) ? null : await sessions.GetModelSettingsJsonAsync(sessionId, ct);
        var settings = json == null ? null : JsonSerializer.Deserialize<SessionModelSettings>(json, Json);
        // A session moved to a different scenario must not reuse that scenario's node overrides.
        return settings != null && string.Equals(settings.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
            ? settings : new SessionModelSettings { ScenarioId = scenarioId };
    }

    public async Task<ModelSelectionPlan> PlanAsync(string? sessionId, string? scenarioId, CancellationToken ct)
    {
        var def = string.IsNullOrWhiteSpace(scenarioId) ? null : scenarios.Get(scenarioId);
        var settings = await SettingsAsync(sessionId, scenarioId, ct);
        return Resolve(def, settings, OllamaRuntimeConfiguration.GetDefaultModel(), configuration["Agctor:LLM:DefaultDecisionModel"]);
    }

    public static ModelSelectionPlan Resolve(ScenarioDefinition? scenario, SessionModelSettings? settings, string applicationLlm, string? applicationDecision = null)
    {
        if (settings != null && !string.Equals(settings.ScenarioId, scenario?.Id, StringComparison.OrdinalIgnoreCase)) settings = null;
        var defaults = new Dictionary<string, ResolvedModel>();
        foreach (var role in ModelRoles.All)
        {
            var chat = Clean(settings?.Defaults?.ForRole(role));
            var sc = Clean(scenario?.ModelDefaults?.ForRole(role));
            defaults[role] = chat != null ? new(role, chat, "chat default")
                : sc != null ? new(role, sc, "scenario default")
                : new(role, role == ModelRoles.DecisionRouting ? Clean(applicationDecision) : Clean(applicationLlm), "application default");
        }
        var nodes = new Dictionary<string, ResolvedModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in scenario?.Flow?.Nodes ?? [])
        {
            var role = Role(node);
            if (role == null) continue;
            var nodeModel = ConfigModel(node, role);
            var overrideEntry = settings?.Nodes?.FirstOrDefault(p => string.Equals(p.Key, node.Id, StringComparison.OrdinalIgnoreCase)).Value;
            nodes[node.Id] = overrideEntry?.Role == role && Clean(overrideEntry.Model) is { } chatNode
                ? new(role, chatNode, "chat node override")
                : nodeModel != null ? new(role, nodeModel, "flow node") : defaults[role];
        }
        return new(scenario?.Id ?? settings?.ScenarioId, defaults, nodes);
    }

    public static string? Role(ScenarioFlowNode node) => node.Type.ToLowerInvariant() switch
    {
        "llmnode" => ModelRoles.Generation,
        "router" => ScenarioFlowRouterConfig.Parse(node.Config).Mode switch
        {
            ScenarioFlowRouterMode.Decision => ModelRoles.DecisionRouting,
            ScenarioFlowRouterMode.Llm => ModelRoles.LlmRouting,
            _ => null
        },
        _ => null
    };
    public static string? ConfigModel(ScenarioFlowNode node, string role)
    {
        return node.Config is { ValueKind: JsonValueKind.Object } config
            && config.TryGetProperty(role == ModelRoles.DecisionRouting ? "decisionModel" : "llmModel", out var value)
            && value.ValueKind == JsonValueKind.String ? Clean(value.GetString()) : null;
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Carry the snapshot explicitly across the actor mailbox, where AsyncLocal does not flow.
    public static string ExecutionJson(ScenarioFlowDocument flow, ModelSelectionPlan? plan)
    {
        var json = JsonSerializer.SerializeToNode(flow, ScenarioFlowJson.Options)!.AsObject();
        if (plan != null) json["_modelPlan"] = JsonSerializer.SerializeToNode(plan, Json);
        return json.ToJsonString();
    }
    public static ModelSelectionPlan? ReadExecutionPlan(string flowJson)
    {
        using var json = JsonDocument.Parse(flowJson);
        return json.RootElement.TryGetProperty("_modelPlan", out var plan) ? plan.Deserialize<ModelSelectionPlan>(Json) : null;
    }
}
