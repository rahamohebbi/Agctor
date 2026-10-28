using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using AgctorSDK.Core.SceneMemory;
using AgctorSDK.Core.Tools.Models;

namespace AgctorSDK.Core.Agents;

/// <summary>A deterministic test agent that retrieves facts through the normal tool mailbox path. No LLM.</summary>
public sealed class SceneMemoryTestAgent : Agent
{
    private readonly IAgentFactory _factory;
    /// <summary>The fixed workspace attached when this agent is created.</summary>
    public string WorkspaceId { get; }
    /// <inheritdoc />
    public override string Name { get; }
    /// <inheritdoc />
    public override string Description => "Read-only Scene Memory test agent; deterministic tool retrieval, no model.";

    /// <summary>Creates a test agent bound to one workspace.</summary>
    public SceneMemoryTestAgent(string id, string name, string workspaceId, IAgentFactory factory) : base(id)
    {
        Name = name;
        WorkspaceId = workspaceId;
        _factory = factory;
        SetAgentFactory(factory);
    }

    /// <inheritdoc />
    public override async Task<IMessageEnvelope> ReceiveAsync(IMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var query = envelope.Payload switch
        {
            SceneQuery typed => typed,
            ProcessPromptMessage prompt => new SceneQuery(Question: prompt.Prompt),
            _ => null
        };
        if (query == null) return await base.ReceiveAsync(envelope, cancellationToken).ConfigureAwait(false);
        var parameters = new Dictionary<string, object> { ["workspaceId"] = WorkspaceId };
        if (query.EntityId != null) parameters["entityId"] = query.EntityId;
        if (query.Question != null) parameters["question"] = query.Question;
        if (query.AtSequence.HasValue) parameters["atSequence"] = query.AtSequence.Value;
        var result = await _factory.InvokeToolRequestAsync(SceneMemoryTool.TypeName,
            new ToolRequest { ToolName = SceneMemoryTool.TypeName, Operation = "Query", Parameters = parameters },
            Id, cancellationToken).ConfigureAwait(false);
        return new MessageEnvelope(result);
    }
}
