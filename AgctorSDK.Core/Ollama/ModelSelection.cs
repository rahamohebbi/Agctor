using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace AgctorSDK.Core.Ollama;

public sealed class ModelDefaults
{
    public string? Generation { get; set; }
    public string? LlmRouting { get; set; }
    public string? DecisionRouting { get; set; }
    public string? ForRole(string role) => role switch
    {
        ModelRoles.Generation => Generation,
        ModelRoles.LlmRouting => LlmRouting,
        ModelRoles.DecisionRouting => DecisionRouting,
        _ => throw new ArgumentException("Unknown model role.", nameof(role))
    };
}

public static class ModelRoles
{
    public const string Generation = "generation";
    public const string LlmRouting = "llmRouting";
    public const string DecisionRouting = "decisionRouting";
    public static readonly string[] All = { Generation, LlmRouting, DecisionRouting };
}

public sealed record NodeModelOverride(string Role, string Model);
public sealed class SessionModelSettings
{
    public string? ScenarioId { get; set; }
    public ModelDefaults Defaults { get; set; } = new();
    public Dictionary<string, NodeModelOverride> Nodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record ResolvedModel(string Role, string? Model, string Source);

/// <summary>A value snapshot created once per message; safe to carry across actor boundaries.</summary>
public sealed record ModelSelectionPlan(
    string? ScenarioId,
    IReadOnlyDictionary<string, ResolvedModel> Defaults,
    IReadOnlyDictionary<string, ResolvedModel> Nodes);

/// <summary>Async-flow-local model selection. Never mutates the application's global Ollama defaults.</summary>
public static class ModelSelectionScope
{
    private sealed record State(ModelSelectionPlan? Plan, ResolvedModel? Selection);
    private static readonly AsyncLocal<State?> Current = new();
    public static ModelSelectionPlan? Plan => Current.Value?.Plan;
    public static ResolvedModel? Selection => Current.Value?.Selection;
    public static string GenerationModel => Selection?.Model
        ?? (Plan?.Defaults.TryGetValue(ModelRoles.Generation, out var model) == true ? model.Model : null)
        ?? OllamaRuntimeConfiguration.GetDefaultModel();

    public static IDisposable Begin(ModelSelectionPlan? plan) => Push(new State(plan, null));
    public static IDisposable Select(ResolvedModel selection) => Push(new State(Plan, selection));
    public static IDisposable Node(string nodeId, string role, string? explicitModel = null)
    {
        var node = Plan?.Nodes.FirstOrDefault(p => p.Key.Equals(nodeId, StringComparison.OrdinalIgnoreCase)).Value;
        var resolved = node != null ? node
            : !string.IsNullOrWhiteSpace(explicitModel) ? new ResolvedModel(role, explicitModel.Trim(), "node")
            : Plan?.Defaults.TryGetValue(role, out var fallback) == true ? fallback
            : new ResolvedModel(role, role == ModelRoles.DecisionRouting ? null : OllamaRuntimeConfiguration.GetDefaultModel(), "application");
        return Select(resolved);
    }
    private static IDisposable Push(State state)
    {
        var previous = Current.Value;
        Current.Value = state;
        return new Restore(() => Current.Value = previous);
    }
    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
