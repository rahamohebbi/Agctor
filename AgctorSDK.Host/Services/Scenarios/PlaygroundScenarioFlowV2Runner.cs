using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Host.Models;

namespace AgctorSDK.Host.Services.Scenarios;

public sealed class PlaygroundScenarioFlowV2Result
{
    public bool Handled { get; init; }
    public bool Success { get; init; }
    public bool Completed { get; init; }
    public string AssistantText { get; init; } = string.Empty;
    public string? Status { get; init; }
    public string? ExecutionNodeId { get; init; }
}

/// <summary>Runs PRD-024 v2 scenario flows from the playground SSE stream.</summary>
public sealed class PlaygroundScenarioFlowV2Runner
{
    private readonly IScenarioFlowExecutionService _flowExecution;

    public PlaygroundScenarioFlowV2Runner(IScenarioFlowExecutionService flowExecution)
    {
        _flowExecution = flowExecution;
    }

    public static bool AppliesTo(ScenarioFlowDocument? flow) =>
        flow != null
        && ScenarioFlowCapabilities.RequiresRuntimeActor(
            flow.SchemaVersion,
            flow.Nodes.Select(n => n.Type),
            (flow.Edges ?? new List<ScenarioFlowEdge>()).Select(e => e.Mode));

    public async Task<PlaygroundScenarioFlowV2Result> RunAsync(
        string scenarioId,
        string sessionId,
        string userMessage,
        IReadOnlyList<string> attachmentIds,
        CancellationToken cancellationToken)
    {
        var run = await _flowExecution
            .RunAsync(
                scenarioId,
                new ScenarioFlowRunRequest
                {
                    Message = userMessage,
                    SessionId = sessionId,
                    AttachmentIds = attachmentIds.Count > 0 ? attachmentIds.ToList() : null,
                    LlmNodeTimeoutSeconds = 600
                },
                cancellationToken)
            .ConfigureAwait(false);

        // Belt-and-suspenders: if still waiting for photos but attachments were sent this turn, resume once.
        if (!run.Completed
            && string.Equals(run.Status, ScenarioFlowRuntimeStatus.WaitingForUserInput.ToString(), StringComparison.OrdinalIgnoreCase)
            && attachmentIds.Count > 0)
        {
            run = await _flowExecution
                .RunAsync(
                    scenarioId,
                    new ScenarioFlowRunRequest
                    {
                        Message = userMessage,
                        SessionId = sessionId,
                        AttachmentIds = attachmentIds.ToList(),
                        LlmNodeTimeoutSeconds = 600
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!run.Success)
        {
            var err = run.ErrorMessage ?? run.ErrorCode ?? "Scenario flow failed.";
            return new PlaygroundScenarioFlowV2Result
            {
                Handled = true,
                Success = false,
                AssistantText = "Error: " + err
            };
        }

        var text = run.Completed
            ? run.Output ?? string.Empty
            : run.Output ?? run.PendingPrompt ?? ScenarioFlowSuspendFallback(run.Status);

        return new PlaygroundScenarioFlowV2Result
        {
            Handled = true,
            Success = true,
            Completed = run.Completed,
            AssistantText = text,
            Status = run.Status,
            ExecutionNodeId = run.ExecutionNodeId
        };
    }

    private static string ScenarioFlowSuspendFallback(string? status)
    {
        if (string.Equals(status, ScenarioFlowRuntimeStatus.WaitingForDomainEvent.ToString(), StringComparison.OrdinalIgnoreCase))
            return "Thanks for the photos — analyzing them now. Style advice will follow shortly.";

        return "Waiting for your input.";
    }
}
