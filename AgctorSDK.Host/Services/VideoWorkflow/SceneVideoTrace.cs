using System.Diagnostics;
using System.Text.Json;
using AgctorSDK.Core.Workflows;

namespace AgctorSDK.Host.Services.VideoWorkflow;

/// <summary>Invocation-scoped diagnostics. No authoritative actor state, prompts, credentials or URLs.</summary>
public static class SceneVideoTrace
{
    private sealed record Context(IAgentRunStore Store, AgentRunLease Lease, string Role, string Phase, ILogger Log);
    private static readonly AsyncLocal<Context?> Current = new();
    public static bool IsActive => Current.Value != null;
    public static IDisposable Begin(IAgentRunStore store, AgentRunLease lease, string role, ILogger log)
    {
        var previous = Current.Value;
        Current.Value = new(store, lease, role, lease.Run.Phase, log);
        return new Scope(() => Current.Value = previous);
    }
    private sealed class Scope(Action close) : IDisposable { public void Dispose() => close(); }
    public static async Task<T> InvokeAsync<T>(string target, string kind, string detail, string operation,
        Func<Task<T>> action, Func<T,string>? outcome = null)
    {
        var context = Current.Value;
        if (context == null) return await action();
        var exchange = Guid.NewGuid().ToString("N"); var watch = Stopwatch.StartNew();
        await Write(context, operation, new(context.Role, target, context.Phase, exchange,
            ToKind: kind, ToDetail: detail, Operation: operation, Outcome: "started"));
        try {
            var result = await action();
            var status = outcome?.Invoke(result) ?? "completed";
            await Write(context, status == "completed" ? operation + " completed." : operation + " returned an error.",
                new(target, context.Role, context.Phase, exchange, FromKind: kind, FromDetail: detail,
                    Operation: operation, Outcome: status, DurationMs: watch.ElapsedMilliseconds));
            return result;
        } catch (Exception ex) {
            await Write(context, operation + (ex is OperationCanceledException ? " was interrupted." : " failed."),
                new(target, context.Role, context.Phase, exchange, FromKind: kind, FromDetail: detail,
                    Operation: operation, Outcome: ex is OperationCanceledException ? "cancelled" : "failed", DurationMs: watch.ElapsedMilliseconds));
            throw;
        }
    }
    public static async Task ReportModelAsync(string tool, string model, string detail)
    {
        var context = Current.Value;
        if (context == null || string.IsNullOrWhiteSpace(model) || model.Length > 200) return;
        await Write(context, "Cynelo reported model evidence: " + model + ".", new(tool, model, context.Phase,
            Guid.NewGuid().ToString("N"), FromKind: "tool", ToKind: "model", ToDetail: detail,
            Operation: "Reported model evidence", Outcome: "reported"));
    }
    private static async Task Write(Context context, string message, AgentMessageRoute route)
    {
        try { await context.Store.ActivityAsync(context.Lease, context.Role, message, default, route); }
        catch (Exception) {
            // Diagnostics must never turn a completed external operation into a retry.
            context.Log.LogWarning("Invocation tracing was unavailable for run {RunId}", context.Lease.Run.Id);
        }
    }
}

/// <summary>Observes actual requests made by the workflow's named clients, including model repair calls.</summary>
public sealed class SceneVideoTraceHandler(string category) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (!SceneVideoTrace.IsActive) return await base.SendAsync(request, ct);
        var path = request.RequestUri?.AbsolutePath ?? "";
        string target, kind, detail, operation;
        if (category == "ollama") {
            string? model = null;
            try { if (request.Content != null) {
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                if (body.RootElement.TryGetProperty("model", out var value) && value.ValueKind == JsonValueKind.String) model = value.GetString();
            }
            } catch (JsonException) { return await base.SendAsync(request, ct); }
            // Exact requested model, not a configured default or a guessed model.
            if (string.IsNullOrWhiteSpace(model) || model.Length > 200) return await base.SendAsync(request, ct);
            target = "Ollama · " + model; kind = "llm"; detail = model; operation = "Request model response";
        } else {
            kind = "tool"; detail = "Cynelo API";
            (target, operation) = path switch {
                _ when path.EndsWith("/speech") => ("Speech inspection", "Inspect saved clip speech"),
                _ when path.EndsWith("/evidence") => ("Frame sampler", "Read sampled video frames"),
                _ when path.EndsWith("/audio") => ("Audio production", "Prepare story audio tracks"),
                _ when path.EndsWith("/finish") => ("Film assembly", "Assemble approved film"),
                _ when path.EndsWith("/project") => ("Project storage", "Prepare story project"),
                _ when path.Contains("/stories") => ("Story storage", request.Method == HttpMethod.Get ? "List stories" : "Prepare story memory"),
                _ => ("Video rendering", request.Method == HttpMethod.Get ? "Check existing render" : "Submit approved clip")
            };
        }
        var response = await SceneVideoTrace.InvokeAsync(target, kind, detail, operation,
            () => base.SendAsync(request, ct), r => r.IsSuccessStatusCode ? "completed" : "failed");
        // Returned evidence can name downstream models. It is not presented as a live model invocation.
        if (category != "ollama" && response.IsSuccessStatusCode && (path.EndsWith("/audio") || path.EndsWith("/speech"))) {
            try {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = body.RootElement;
                if (root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
                    await SceneVideoTrace.ReportModelAsync(target, model.GetString()!, "Reported by Cynelo; may be cached");
                if (root.TryGetProperty("evidence", out var evidence)) {
                    if (evidence.TryGetProperty("separator", out var separator) && separator.ValueKind == JsonValueKind.Object && separator.TryGetProperty("model", out model))
                        await SceneVideoTrace.ReportModelAsync(target, model.GetString()!, "Separation evidence · reported by Cynelo");
                    if (evidence.TryGetProperty("narration", out var narration) && narration.ValueKind == JsonValueKind.Array)
                        foreach (var name in narration.EnumerateArray().Where(n=>n.TryGetProperty("model",out _)).Select(n=>n.GetProperty("model").GetString()).Distinct())
                            await SceneVideoTrace.ReportModelAsync(target, name!, "Narration evidence · reported by Cynelo");
                }
            } catch (Exception) { /* Optional diagnostics cannot affect the returned tool result. */ }
        }
        return response;
    }
}
