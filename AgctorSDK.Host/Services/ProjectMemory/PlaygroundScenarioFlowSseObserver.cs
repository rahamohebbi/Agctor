using System.Text.Json;
using AgctorSDK.Core.Streaming;
using AgctorSDK.Host.Services.Scenarios;

namespace AgctorSDK.Host.Services.ProjectMemory;

/// <summary>Maps <see cref="ScenarioFlowGraphInterpreter"/> lifecycle to playground SSE (<c>flow_step</c>, <c>flow_plan_tail</c>).</summary>
internal sealed class PlaygroundScenarioFlowSseObserver : IScenarioFlowExecutionObserver
{
    private readonly ScenarioFlowDocument _flow;
    private readonly bool _ingestChipActive;
    private readonly Func<AgentStreamEvent, Task> _writeSse;
    private readonly string _agentIdForEvents;
    private readonly JsonSerializerOptions _json;

    public PlaygroundScenarioFlowSseObserver(
        ScenarioFlowDocument flow,
        bool ingestChipActive,
        Func<AgentStreamEvent, Task> writeSse,
        string agentIdForEvents,
        JsonSerializerOptions json)
    {
        _flow = flow;
        _ingestChipActive = ingestChipActive;
        _writeSse = writeSse;
        _agentIdForEvents = agentIdForEvents;
        _json = json;
    }

    public async Task OnNodeStartingAsync(string nodeId, string nodeType, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { id = nodeId, status = "running", detail = (string?)null }, _json);
        await _writeSse(new AgentStreamEvent { Type = "flow_step", Payload = payload, AgentId = _agentIdForEvents })
            .ConfigureAwait(false);
    }

    public async Task OnNodeCompletedAsync(string nodeId, string nodeType, string? detail, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { id = nodeId, status = "done", detail }, _json);
        await _writeSse(new AgentStreamEvent { Type = "flow_step", Payload = payload, AgentId = _agentIdForEvents })
            .ConfigureAwait(false);
    }

    public async Task OnRouterBranchResolvedAsync(
        string routerNodeId,
        IReadOnlyList<string> orderedEntryNodeIds,
        string? mergeNodeIdForParallel,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PlaygroundFlowPlanBuilder.Step> tailSteps;
        if (!string.IsNullOrWhiteSpace(mergeNodeIdForParallel) && orderedEntryNodeIds.Count > 1)
        {
            tailSteps = PlaygroundFlowPlanBuilder.BuildFlowExecutionPlanParallelTail(
                _flow,
                orderedEntryNodeIds,
                mergeNodeIdForParallel!,
                _ingestChipActive);
        }
        else if (orderedEntryNodeIds.Count == 1)
        {
            tailSteps = PlaygroundFlowPlanBuilder.BuildFlowExecutionPlanLinearTail(
                _flow,
                orderedEntryNodeIds[0],
                _ingestChipActive);
        }
        else
        {
            tailSteps = Array.Empty<PlaygroundFlowPlanBuilder.Step>();
        }

        var planPayload = new
        {
            steps = tailSteps
                .Select(s => new { id = s.Id, label = s.Label, optional = s.Optional, active = s.Active })
                .ToArray()
        };
        await _writeSse(new AgentStreamEvent
            {
                Type = "flow_plan_tail",
                Payload = JsonSerializer.Serialize(planPayload, _json),
                AgentId = _agentIdForEvents
            })
            .ConfigureAwait(false);
    }
}
