using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.ProjectMemory;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Tools;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Tools.Implementations;

/// <summary>Read-only actor adapter for the independent Scene Memory store.</summary>
[AgctorHostTool("scene-memory-query", "Scene Memory query",
    "Retrieves manually entered scene facts. Query requires workspaceId and entityId or query (question is an alias); optional atSequence. No model required.", DefaultOperation = "Query")]
public sealed class SceneMemoryQueryTool : ToolActorBase
{
    private readonly ISceneMemoryProvider _provider;

    /// <summary>Uses the existing Host service bridge for runtime-created tools.</summary>
    public SceneMemoryQueryTool(string id) : this(id, ProjectMemoryServiceAccessor.GetRequiredService<ISceneMemoryProvider>()) { }

    /// <summary>Explicit provider injection for standalone callers and tests.</summary>
    public SceneMemoryQueryTool(string id, ISceneMemoryProvider provider) : base(id, SceneMemoryTool.TypeName) =>
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>Compatibility overload for callers that previously supplied a store.</summary>
    public SceneMemoryQueryTool(string id, ISceneMemoryStore store) : this(id, new SceneMemoryProvider(store)) { }

    /// <inheritdoc />
    protected override Task<ToolResult> OnProcessPromptAsync(string prompt, CancellationToken cancellationToken) =>
        Task.FromResult(new ToolResult { Error = "Use Query with workspaceId and entityId or query (question is an alias)." });

    /// <inheritdoc />
    public override Task<ToolResult> Handle(ToolRequest request) => QueryAsync(request, CancellationToken.None);

    /// <inheritdoc />
    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default) =>
        envelope.Payload is ToolRequest request ? new MessageEnvelope(await QueryAsync(request, cancellationToken).ConfigureAwait(false))
            : await base.ReceiveAsync(envelope, cancellationToken).ConfigureAwait(false);

    private async Task<ToolResult> QueryAsync(ToolRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.Operation, "Query", StringComparison.OrdinalIgnoreCase))
            return new ToolResult { Error = "Scene Memory query is read-only; only Query is supported." };
        string? Get(string key) => request.Parameters != null && request.Parameters.TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        try
        {
            var workspaceId = Get("workspaceId");
            if (string.IsNullOrWhiteSpace(workspaceId)) throw new ArgumentException("workspaceId is required.");
            long? sequence = null;
            if (Get("atSequence") is { Length: > 0 } raw)
            {
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    throw new ArgumentException("atSequence must be an integer.");
                sequence = parsed;
            }
            return new ToolResult { IsSuccess = true, Output = await _provider.QueryAsync(
                new SceneMemoryQueryRequest(workspaceId, Get("query") ?? Get("question"), Get("entityId"), sequence), ct).ConfigureAwait(false) };
        }
        catch (Exception ex) when (ex is ArgumentException or System.Collections.Generic.KeyNotFoundException)
        {
            return new ToolResult { Error = ex.Message };
        }
    }
}
