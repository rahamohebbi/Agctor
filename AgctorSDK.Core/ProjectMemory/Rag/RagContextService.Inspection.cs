using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgctorSDK.Core.Ollama;
using AgctorSDK.Core.Rag;
using AgctorSDK.Core.Utils.ActivityTracking;

namespace AgctorSDK.Core.ProjectMemory.Rag;

public sealed partial class RagContextService
{
    private readonly IRagInspectionStore? _inspectionStore;
    private readonly IActivityTracker? _activityTracker;
    private static readonly AsyncLocal<RagInspectionRecord?> ActiveInspection = new();

    internal void NoteContextTruncated()
    {
        if (ActiveInspection.Value is { } record) record.ContextTruncated = true;
    }

    private RagProviderSelection ResolveProvider(string? explicitProvider)
    {
        var selected = RagProviderSelection.Resolve(explicitProvider, ModelSelectionScope.Plan?.RagProvider, _factory.GetDefaultProviderId());
        // Tool mailboxes carry the already-resolved provider and its original source.
        return RagInspectionScope.Current?.ProviderSource is { } source ? selected with { Source = source } : selected;
    }

    public async Task<string> InspectContextAsync(string agentId, string? scenarioId, string strategy,
        string query, string? provider, Func<Task<string>> build)
    {
        var correlation = RagInspectionScope.Current ?? ModelSelectionScope.Plan?.RagInspection;
        if (correlation == null || _inspectionStore == null) return await build();
        var selection = ResolveProvider(provider);
        var record = new RagInspectionRecord
        {
            Correlation = correlation with { AgentId = agentId }, ScenarioId = scenarioId,
            ProviderId = selection.ProviderId, ProviderSource = selection.Source, Strategy = strategy, Query = query,
            Outcome = strategy is "rag" or "graph_rag" ? "running" : "not_requested"
        };
        record.Stages.Add(new("Provider selected", "complete", 0, selection.Source));
        var previous = ActiveInspection.Value;
        ActiveInspection.Value = record;
        var timer = Stopwatch.StartNew();
        using var activity = _activityTracker?.StartActivity("rag.context");
        void Detail() => activity?.SetTimelineDetailJson(JsonSerializer.Serialize(new
        {
            kind = "rag.retrieval", sessionId = correlation.SessionId, turnGroupId = correlation.TurnGroupId,
            ragInspectionId = record.Id, provider = record.ProviderId, status = record.Outcome
        }));
        Detail();
        await SaveInspectionAsync(record);
        try
        {
            var context = await build();
            record.PreparedContext = context.Length <= 256_000 ? context : context[..256_000];
            record.ContextPreviewTruncated = context.Length > 256_000;
            record.MarkdownFallback = strategy is "rag" or "graph_rag" && record.Outcome != "success";
            if (record.Outcome == "not_requested") record.Reason = $"This step uses {strategy}; no external retrieval was requested.";
            record.Stages.Add(new("Context prepared", "complete", 0,
                record.MarkdownFallback ? "Focused markdown fallback" : $"{context.Length:N0} characters"));
            return context;
        }
        catch (OperationCanceledException)
        {
            record.Outcome = "cancelled"; record.Reason = "Context preparation was cancelled.";
            throw;
        }
        catch (Exception ex)
        {
            record.Outcome = "failed"; record.Reason = ex.Message;
            activity?.SetStatus(ActivityStatus.Error, ex.Message);
            throw;
        }
        finally
        {
            record.CompletedAt = DateTimeOffset.UtcNow;
            record.DurationMs = timer.Elapsed.TotalMilliseconds;
            Detail();
            await SaveInspectionAsync(record);
            ActiveInspection.Value = previous;
        }
    }

    public Task<string> RecordNotRequestedAsync(string agentId, string? scenarioId) =>
        InspectContextAsync(agentId, scenarioId, "no memory context", "", null, () => Task.FromResult(""));

    private async Task<T> InspectStageAsync<T>(string name, Func<Task<T>> action)
    {
        var record = ActiveInspection.Value;
        if (record == null) return await action();
        var index = record.Stages.Count;
        record.Stages.Add(new(name, "running", 0));
        await SaveInspectionAsync(record);
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await action();
            record.Stages[index] = new(name, result is RagQueryResult { Error: not null } ? "failed" : "complete", timer.Elapsed.TotalMilliseconds,
                result is RagHealthResult health ? health.Status + ": " + health.Message : (result as RagQueryResult)?.Error);
            return result;
        }
        catch (Exception ex)
        {
            record.Stages[index] = new(name, ex is OperationCanceledException ? "cancelled" : "failed", timer.Elapsed.TotalMilliseconds, ex.Message);
            throw;
        }
        finally { await SaveInspectionAsync(record); }
    }

    private async Task SaveInspectionAsync(RagInspectionRecord record)
    {
        // Diagnostics must never prevent a response. A deleted session cannot be recreated here.
        try { if (_inspectionStore != null) await _inspectionStore.SaveRagInspectionAsync(record, CancellationToken.None); }
        catch (Exception ex) { _activityTracker?.StartActivity("rag.inspection-save-failed")?.Dispose(); System.Diagnostics.Debug.WriteLine(ex.Message); }
    }

    private static bool IsSecret(string key) => key.Replace("-", "").Replace("_", "").ToLowerInvariant()
        is "authorization" or "apikey" or "accesstoken" or "refreshtoken" or "password" or "secret" or "cookie" or "setcookie" or "token" or "clientsecret" or "privatekey";
    private static IReadOnlyDictionary<string, string>? SafeMetadata(IReadOnlyDictionary<string, string>? values) =>
        values?.Take(20).ToDictionary(p => p.Key, p => IsSecret(p.Key) ? "[redacted]" : p.Value[..Math.Min(p.Value.Length, 200)]);
    private static string? SafeRawJson(string? json, out bool truncated)
    {
        truncated = false;
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (json.Length > 128_000) { truncated = true; return "Raw response omitted: exceeds 128,000 characters."; }
        try
        {
            var node = JsonNode.Parse(json);
            void Redact(JsonNode? value)
            {
                if (value is JsonObject obj)
                    foreach (var key in obj.Select(p => p.Key).ToArray())
                        if (IsSecret(key)) obj[key] = "[redacted]"; else Redact(obj[key]);
                else if (value is JsonArray arr) foreach (var item in arr) Redact(item);
            }
            Redact(node);
            return node?.ToJsonString();
        }
        catch (JsonException) { return "Provider response was not valid JSON; raw capture omitted."; }
    }
}
