using System.Text.Json;
using AgctorSDK.Core.ProjectMemory.Scenarios;
using AgctorSDK.Host.Services.ProjectMemory;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>PRD-024 Host implementation: deserializes graph and runs one segment.</summary>
public sealed class ScenarioFlowSegmentExecutor : IScenarioFlowSegmentExecutor
{
    private readonly IScenarioFlowPersonaLlmRunner _flowPersonaRunner;
    private readonly IScenarioFlowRouterLlmService _routerLlm;

    public ScenarioFlowSegmentExecutor(
        IScenarioFlowPersonaLlmRunner flowPersonaRunner,
        IScenarioFlowRouterLlmService routerLlm)
    {
        _flowPersonaRunner = flowPersonaRunner;
        _routerLlm = routerLlm;
    }

    public async Task<ScenarioFlowSegmentResult> RunSegmentAsync(
        ScenarioFlowSegmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ScenarioFlowDocument? flow;
        try
        {
            flow = JsonSerializer.Deserialize<ScenarioFlowDocument>(request.FlowJson, ScenarioFlowJson.Options);
        }
        catch (Exception ex)
        {
            return Fail(request.Snapshot, $"Invalid flow JSON: {ex.Message}");
        }

        if (flow?.Nodes == null || flow.Nodes.Count == 0)
            return Fail(request.Snapshot, "Flow has no nodes.");

        var snapshot = request.Snapshot;
        var startNodeId = ResolveStartNodeId(flow, snapshot, request.UserMessage, request.AttachmentIds);
        var nodeMap = flow.Nodes.ToDictionary(n => n.Id.Trim(), n => n, StringComparer.OrdinalIgnoreCase);

        var store = ScenarioFlowGraphInterpreter.BuildTextStore(snapshot);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var interpreter = new ScenarioFlowGraphInterpreter();

        try
        {
            var segment = await interpreter.ExecuteSegmentAsync(
                flow,
                startNodeId,
                store,
                completed,
                snapshot,
                request.UserMessage,
                async (personaId, prompt, ct, flowNodeId) =>
                {
                    if (ShouldSkipVisualIntakeLlm(personaId, snapshot))
                        return BuildVisualIntakeAttachmentAck();

                    JsonElement? nodeConfig = null;
                    if (!string.IsNullOrWhiteSpace(flowNodeId)
                        && nodeMap.TryGetValue(flowNodeId.Trim(), out var flowNode))
                    {
                        nodeConfig = flowNode.Config;
                    }

                    var relaxVisual = ShouldRelaxVisualEntityFilter(personaId, snapshot);
                    var sessionAssets = relaxVisual ? ResolveSessionAssetIds(snapshot) : null;
                    var r = await _flowPersonaRunner
                        .RunFlowNodeAsync(
                            new ScenarioFlowPersonaRunRequest
                            {
                                ProjectRoot = request.ProjectRoot,
                                ScenarioId = request.ScenarioId,
                                SessionId = request.SessionId,
                                AgentId = personaId,
                                InputText = prompt,
                                FlowNodeId = flowNodeId,
                                FlowNodeConfig = nodeConfig,
                                Snapshot = snapshot,
                                RelaxVisualEntityFilter = relaxVisual,
                                SessionAssetIds = sessionAssets
                            },
                            ct)
                        .ConfigureAwait(false);
                    if (!r.Ok)
                        throw new ScenarioFlowExecutionException(r.ErrorMessage ?? "LlmNode failed.");
                    return r.OutputText ?? string.Empty;
                },
                request.LlmNodeTimeout,
                request.ProjectRoot,
                _routerLlm,
                cancellationToken).ConfigureAwait(false);

            return new ScenarioFlowSegmentResult
            {
                Outcome = segment.Outcome,
                Snapshot = segment.Snapshot,
                Output = segment.Output,
                ErrorMessage = segment.ErrorMessage
            };
        }
        catch (ScenarioFlowExecutionException ex)
        {
            return Fail(snapshot, ex.Message);
        }
    }

    private static bool ShouldRelaxVisualEntityFilter(string personaId, ScenarioFlowRuntimeSnapshot snapshot) =>
        string.Equals(personaId, "style-coach", StringComparison.OrdinalIgnoreCase)
        && snapshot.Store.Facts.TryGetValue("visual.hasPhotos", out var has)
        && has is true;

    private static IReadOnlyList<string>? ResolveSessionAssetIds(ScenarioFlowRuntimeSnapshot snapshot)
    {
        var ids = snapshot.Store.Attachments.AllInRun;
        return ids.Count > 0 ? ids : null;
    }

    private static string ResolveStartNodeId(
        ScenarioFlowDocument flow,
        ScenarioFlowRuntimeSnapshot snapshot,
        string userMessage,
        IReadOnlyList<string> attachmentIds)
    {
        if (snapshot.Status == ScenarioFlowRuntimeStatus.WaitingForUserInput
            && !string.IsNullOrWhiteSpace(snapshot.ExecutionNodeId))
        {
            return ScenarioFlowGraphInterpreter.ResolveResumeTargetNode(flow, snapshot.ExecutionNodeId);
        }

        if (snapshot.Status == ScenarioFlowRuntimeStatus.WaitingForDomainEvent
            && !string.IsNullOrWhiteSpace(snapshot.ExecutionNodeId))
        {
            return ScenarioFlowGraphInterpreter.ResolveResumeTargetNode(flow, snapshot.ExecutionNodeId);
        }

        if (snapshot.Status == ScenarioFlowRuntimeStatus.Running
            && attachmentIds.Count > 0
            && !string.IsNullOrWhiteSpace(snapshot.ExecutionNodeId)
            && AcceptsAttachmentWaitNode(flow, snapshot.ExecutionNodeId))
        {
            return ScenarioFlowGraphInterpreter.ResolveResumeTargetNode(flow, snapshot.ExecutionNodeId);
        }

        if (snapshot.Status == ScenarioFlowRuntimeStatus.Running
            && !string.IsNullOrWhiteSpace(snapshot.ExecutionNodeId)
            && IsAwaitEventNode(flow, snapshot.ExecutionNodeId))
        {
            return ScenarioFlowGraphInterpreter.ResolveResumeTargetNode(flow, snapshot.ExecutionNodeId);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ExecutionNodeId))
            return snapshot.ExecutionNodeId.Trim();

        var chatInput = flow.Nodes.FirstOrDefault(n =>
            string.Equals(n.Type, "ChatInput", StringComparison.OrdinalIgnoreCase));
        if (chatInput == null || string.IsNullOrWhiteSpace(chatInput.Id))
            throw new ScenarioFlowExecutionException("Flow has no ChatInput node.");

        _ = userMessage;
        return chatInput.Id.Trim();
    }

    private static ScenarioFlowSegmentResult Fail(ScenarioFlowRuntimeSnapshot snapshot, string error) =>
        new()
        {
            Outcome = ScenarioFlowSegmentOutcome.Failed,
            Snapshot = snapshot,
            ErrorMessage = error
        };

    private static bool ShouldSkipVisualIntakeLlm(string personaId, ScenarioFlowRuntimeSnapshot snapshot)
    {
        if (!string.Equals(personaId, "visual-intake", StringComparison.OrdinalIgnoreCase))
            return false;

        if (snapshot.Store.Attachments.NewSinceLastResume.Count > 0)
            return true;

        return snapshot.Store.Facts.TryGetValue("user.hasAttachments", out var att)
               && att is bool hasAttachments
               && hasAttachments;
    }

    private static string BuildVisualIntakeAttachmentAck() =>
        "Thanks for the photos — I'm analyzing them now. You'll see insights on each image and in the confirmation inbox when ready.";

    private static bool AcceptsAttachmentWaitNode(ScenarioFlowDocument flow, string nodeId)
    {
        var node = flow.Nodes?.FirstOrDefault(n =>
            string.Equals(n.Id, nodeId.Trim(), StringComparison.OrdinalIgnoreCase));
        if (node == null || !string.Equals(node.Type, "WaitForInput", StringComparison.OrdinalIgnoreCase))
            return false;

        if (node.Config is not { ValueKind: JsonValueKind.Object } cfg)
            return false;

        return cfg.TryGetProperty("acceptAttachments", out var flag)
               && flag.ValueKind == JsonValueKind.True;
    }

    private static bool IsAwaitEventNode(ScenarioFlowDocument flow, string nodeId) =>
        flow.Nodes?.Any(n =>
            string.Equals(n.Id, nodeId.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(n.Type, "AwaitEvent", StringComparison.OrdinalIgnoreCase)) == true;
}
