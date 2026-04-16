using System.Text.Json;

namespace AgctorSDK.Host.Services.Scenarios;

/// <summary>
/// Executes a <see cref="ScenarioFlowDocument"/> (PRD-014 Phase 7–10): sequential paths, <see cref="Router"/> (deterministic or LLM),
/// <see cref="PersonaCall"/>, <c>parallel</c> fan-out to branches that meet at one <see cref="Merge"/>, then <see cref="Output"/>.
/// Nested parallel forks from inside a branch are rejected. Persona invocation is injected for tests.
/// </summary>
public sealed class ScenarioFlowGraphInterpreter
{
    /// <summary>Invokes one persona/YAML agent id with the upstream text; returns assistant text.</summary>
    /// <param name="flowNodeId">Graph <see cref="ScenarioFlowNode.Id"/> for this PersonaCall (null in tests that ignore it).</param>
    public delegate Task<string> PersonaInvoker(
        string personaAgentId,
        string promptText,
        CancellationToken cancellationToken,
        string? flowNodeId);

    /// <summary>Walks from the first <c>ChatInput</c> until <c>Output</c> (no LLM Router).</summary>
    public async Task<string> ExecuteAsync(
        ScenarioFlowDocument flow,
        string userMessage,
        PersonaInvoker invokePersona,
        TimeSpan personaCallTimeout,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(flow, userMessage, invokePersona, personaCallTimeout, "", null, null, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Full execution: pass <paramref name="projectRoot"/> and <paramref name="routerLlm"/> when any Router uses <c>routerMode: llm</c>.</summary>
    public async Task<string> ExecuteAsync(
        ScenarioFlowDocument flow,
        string userMessage,
        PersonaInvoker invokePersona,
        TimeSpan personaCallTimeout,
        string projectRoot,
        IScenarioFlowRouterLlmService? routerLlm,
        IScenarioFlowExecutionObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        if (flow.Nodes == null || flow.Nodes.Count == 0)
            throw new ScenarioFlowExecutionException("Flow has no nodes.");

        var map = flow.Nodes.ToDictionary(n => n.Id.Trim(), n => n, StringComparer.OrdinalIgnoreCase);
        var chatInput = flow.Nodes.FirstOrDefault(n =>
            string.Equals(n.Type, "ChatInput", StringComparison.OrdinalIgnoreCase));
        if (chatInput == null || string.IsNullOrWhiteSpace(chatInput.Id))
            throw new ScenarioFlowExecutionException("Flow has no ChatInput node.");

        var store = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = chatInput.Id.Trim();
        var maxSteps = flow.Nodes.Count * 4 + 32;

        for (var step = 0; step < maxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!map.TryGetValue(current, out var node))
                throw new ScenarioFlowExecutionException($"Unknown node id '{current}'.");

            if (string.Equals(node.Type, "Output", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureProcessedAsync(flow, map, store, completed, current, userMessage, invokePersona, personaCallTimeout, cancellationToken, observer)
                    .ConfigureAwait(false);
                return CombineIncoming(flow, store, current);
            }

            await EnsureProcessedAsync(flow, map, store, completed, current, userMessage, invokePersona, personaCallTimeout, cancellationToken, observer)
                .ConfigureAwait(false);

            var parallelOut = OutgoingParallelEdges(flow, current);
            if (parallelOut.Count >= 2)
            {
                if (OutgoingSequentialEdges(flow, current).Count > 0)
                    throw new ScenarioFlowExecutionException($"Node '{current}' mixes parallel and sequential outgoing edges; split with an intermediate node.");

                var targets = parallelOut.Select(e => e.ToNodeId.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (targets.Count != parallelOut.Count)
                    throw new ScenarioFlowExecutionException("Parallel fan-out targets duplicate node ids.");

                var mergeId = FindCommonMergeId(flow, map, targets)
                              ?? throw new ScenarioFlowExecutionException(
                                  "Parallel branches must converge at exactly one shared Merge node (reachable from each branch start).");

                var branchTasks = targets.Select(t =>
                    RunBranchToMergeAsync(flow, map, store, completed, t, mergeId, userMessage, invokePersona, personaCallTimeout, cancellationToken, observer)).ToArray();
                await Task.WhenAll(branchTasks).ConfigureAwait(false);

                await EnsureProcessedAsync(flow, map, store, completed, mergeId, userMessage, invokePersona, personaCallTimeout, cancellationToken, observer)
                    .ConfigureAwait(false);

                current = UniqueOutgoingSequentialOrThrow(flow, mergeId);
                if (string.IsNullOrEmpty(current))
                    throw new ScenarioFlowExecutionException($"No outgoing sequential edge from Merge '{mergeId}'.");

                continue;
            }

            var nodeType = (node.Type ?? string.Empty).Trim();
            if (string.Equals(nodeType, "Router", StringComparison.OrdinalIgnoreCase))
            {
                var rid = current;
                var rCfg = ScenarioFlowRouterConfig.Parse(map[current].Config);
                if (rCfg.Mode == ScenarioFlowRouterMode.Llm)
                {
                    if (routerLlm == null)
                        throw new ScenarioFlowExecutionException(
                            $"Router '{current}' uses routerMode llm but no router LLM service was provided.");

                    if (OutgoingParallelEdges(flow, current).Count > 0)
                        throw new ScenarioFlowExecutionException(
                            $"Router '{current}' (llm) must not have parallel outgoing edges.");

                    var candidates = ListRouterPersonaCandidates(flow, map, current);
                    if (candidates.Count == 0)
                        throw new ScenarioFlowExecutionException(
                            $"Router '{current}' (llm) has no sequential edges to PersonaCall nodes.");

                    var routingText = RouterInputText(flow, store, current, userMessage);
                    var llmResult = await routerLlm
                        .RouteAsync(projectRoot ?? "", userMessage, candidates, rCfg, cancellationToken, routingText)
                        .ConfigureAwait(false);

                    if (llmResult.NeedsClarification)
                    {
                        if (observer != null)
                        {
                            await observer
                                .OnNodeCompletedAsync(rid, "Router", "clarification", cancellationToken)
                                .ConfigureAwait(false);
                        }

                        return string.IsNullOrWhiteSpace(llmResult.ClarificationPrompt)
                            ? "Please clarify your request."
                            : llmResult.ClarificationPrompt!;
                    }

                    if (!llmResult.Ok || llmResult.SelectedPersonaIds == null || llmResult.SelectedPersonaIds.Count == 0)
                        throw new ScenarioFlowExecutionException(llmResult.Error ?? "LLM router failed.");

                    var targetNodes = MapPersonaPicksToNodeIds(candidates, llmResult.SelectedPersonaIds);
                    if (targetNodes.Count == 0)
                        throw new ScenarioFlowExecutionException(
                            "LLM router returned personaIds that do not map to Router candidates.");

                    if (observer != null)
                    {
                        var picked = string.Join(", ", llmResult.SelectedPersonaIds);
                        await observer.OnNodeCompletedAsync(rid, "Router", $"llm→[{picked}]", cancellationToken).ConfigureAwait(false);
                    }

                    if (targetNodes.Count == 1)
                    {
                        if (observer != null)
                        {
                            await observer
                                .OnRouterBranchResolvedAsync(rid, new[] { targetNodes[0] }, null, cancellationToken)
                                .ConfigureAwait(false);
                        }

                        current = await RunLinearBranchUntilOutputAsync(
                                flow,
                                map,
                                store,
                                completed,
                                targetNodes[0],
                                userMessage,
                                invokePersona,
                                personaCallTimeout,
                                cancellationToken,
                                observer)
                            .ConfigureAwait(false);
                        continue;
                    }

                    var mergeId = FindCommonMergeId(flow, map, targetNodes)
                                  ?? throw new ScenarioFlowExecutionException(
                                      "LLM Router: selected PersonaCalls must reach exactly one shared Merge node.");

                    if (observer != null)
                    {
                        await observer
                            .OnRouterBranchResolvedAsync(rid, targetNodes, mergeId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var branchTasks = targetNodes.Select(t =>
                            RunBranchToMergeAsync(
                                flow,
                                map,
                                store,
                                completed,
                                t,
                                mergeId,
                                userMessage,
                                invokePersona,
                                personaCallTimeout,
                                cancellationToken,
                                observer))
                        .ToArray();
                    await Task.WhenAll(branchTasks).ConfigureAwait(false);

                    await EnsureProcessedAsync(
                            flow,
                            map,
                            store,
                            completed,
                            mergeId,
                            userMessage,
                            invokePersona,
                            personaCallTimeout,
                            cancellationToken,
                            observer)
                        .ConfigureAwait(false);

                    current = UniqueOutgoingSequentialOrThrow(flow, mergeId);
                    if (string.IsNullOrEmpty(current))
                        throw new ScenarioFlowExecutionException($"No outgoing sequential edge from Merge '{mergeId}'.");

                    continue;
                }

                current = PickRouterTarget(flow, current, RouterInputText(flow, store, current, userMessage));
                if (observer != null)
                {
                    await observer
                        .OnNodeCompletedAsync(rid, "Router", $"rule→{current}", cancellationToken)
                        .ConfigureAwait(false);
                    await observer
                        .OnRouterBranchResolvedAsync(rid, new[] { current }, null, cancellationToken)
                        .ConfigureAwait(false);
                }

                continue;
            }

            current = UniqueOutgoingSequentialOrThrow(flow, current);
            if (string.IsNullOrEmpty(current))
                throw new ScenarioFlowExecutionException($"No outgoing sequential edge from node '{node.Id}'.");
        }

        throw new ScenarioFlowExecutionException("Flow execution exceeded step limit (possible graph bug).");
    }

    /// <summary>Legacy overload: no per-call timeout (uses <see cref="Timeout.InfiniteTimeSpan"/>).</summary>
    public Task<string> ExecuteAsync(
        ScenarioFlowDocument flow,
        string userMessage,
        PersonaInvoker invokePersona,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(flow, userMessage, invokePersona, Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>Design-time check: shared <see cref="Merge"/> reachable from every branch start (parallel / LLM Router).</summary>
    public static string? FindCommonMergeForBranchStarts(ScenarioFlowDocument flow, IReadOnlyList<string> branchStarts)
    {
        if (flow.Nodes == null || branchStarts.Count == 0)
            return null;
        var map = flow.Nodes.ToDictionary(n => n.Id.Trim(), n => n, StringComparer.OrdinalIgnoreCase);
        return FindCommonMergeId(flow, map, branchStarts);
    }

    /// <summary>Returns true if any edge uses parallel mode (design-time hint; execution supports it since Phase 8).</summary>
    public static bool HasParallelEdges(ScenarioFlowDocument flow)
    {
        foreach (var e in flow.Edges ?? new List<ScenarioFlowEdge>())
        {
            if (IsParallel(e))
                return true;
        }

        return false;
    }

    private static async Task EnsureProcessedAsync(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, ScenarioFlowNode> map,
        Dictionary<string, string> store,
        HashSet<string> completed,
        string nodeId,
        string userMessage,
        PersonaInvoker invokePersona,
        TimeSpan personaTimeout,
        CancellationToken cancellationToken,
        IScenarioFlowExecutionObserver? observer = null)
    {
        if (completed.Contains(nodeId))
            return;

        if (!map.TryGetValue(nodeId, out var node))
            throw new ScenarioFlowExecutionException($"Unknown node id '{nodeId}'.");

        var nodeType = (node.Type ?? string.Empty).Trim();
        if (observer != null)
            await observer.OnNodeStartingAsync(nodeId, nodeType, cancellationToken).ConfigureAwait(false);

        var deferRouterComplete = false;
        string? completeDetail = null;

        switch (nodeType)
        {
            case var t when string.Equals(t, "ChatInput", StringComparison.OrdinalIgnoreCase):
                store[nodeId] = userMessage;
                completeDetail = $"{userMessage.Length} char(s)";
                break;
            case var t when string.Equals(t, "Router", StringComparison.OrdinalIgnoreCase):
                store[nodeId] = GetIncomingText(flow, store, nodeId, userMessage);
                deferRouterComplete = true;
                break;
            case var t when string.Equals(t, "PersonaCall", StringComparison.OrdinalIgnoreCase):
            {
                var prompt = GetIncomingText(flow, store, nodeId, userMessage);
                var pid = TryGetPersonaId(node.Config);
                if (string.IsNullOrWhiteSpace(pid))
                    throw new ScenarioFlowExecutionException($"PersonaCall '{nodeId}' is missing config.personaId.");
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (personaTimeout > TimeSpan.Zero && personaTimeout != Timeout.InfiniteTimeSpan)
                    linked.CancelAfter(personaTimeout);
                try
                {
                    store[nodeId] = await invokePersona(pid.Trim(), prompt, linked.Token, nodeId).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ScenarioFlowExecutionException($"PersonaCall '{nodeId}' timed out after {personaTimeout.TotalSeconds}s.");
                }

                completeDetail = $"{pid.Trim()}; {store[nodeId].Length} char(s)";
                break;
            }
            case var t when string.Equals(t, "Merge", StringComparison.OrdinalIgnoreCase):
                store[nodeId] = CombineIncoming(flow, store, nodeId);
                completeDetail = $"{store[nodeId].Length} char(s) merged";
                break;
            case var t when string.Equals(t, "Output", StringComparison.OrdinalIgnoreCase):
                // Output text is read from predecessors via CombineIncoming at return site; optional store mirror
                store[nodeId] = CombineIncoming(flow, store, nodeId);
                completeDetail = $"{store[nodeId].Length} char(s)";
                break;
            default:
                throw new ScenarioFlowExecutionException($"Unsupported node type '{nodeType}' at '{nodeId}'.");
        }

        if (!deferRouterComplete && observer != null)
            await observer.OnNodeCompletedAsync(nodeId, nodeType, completeDetail, cancellationToken).ConfigureAwait(false);

        completed.Add(nodeId);
    }

    /// <summary>Runs one branch from <paramref name="branchStart"/> along sequential edges until <paramref name="mergeId"/> (exclusive of merge).</summary>
    private static async Task RunBranchToMergeAsync(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, ScenarioFlowNode> map,
        Dictionary<string, string> store,
        HashSet<string> completed,
        string branchStart,
        string mergeId,
        string userMessage,
        PersonaInvoker invokePersona,
        TimeSpan personaTimeout,
        CancellationToken cancellationToken,
        IScenarioFlowExecutionObserver? observer = null)
    {
        var cur = branchStart;
        while (!string.Equals(cur, mergeId, StringComparison.OrdinalIgnoreCase))
        {
            await EnsureProcessedAsync(flow, map, store, completed, cur, userMessage, invokePersona, personaTimeout, cancellationToken, observer)
                .ConfigureAwait(false);

            if (OutgoingParallelEdges(flow, cur).Count >= 2)
                throw new ScenarioFlowExecutionException($"Nested parallel fan-out from '{cur}' is not supported.");

            // Branch path may use a single sequential or parallel edge per hop (e.g. parallel link into Merge).
            var next = UniqueOutgoingSingleEdgeOrThrow(flow, cur);
            if (string.IsNullOrEmpty(next))
                throw new ScenarioFlowExecutionException($"Branch from '{branchStart}' ended before reaching Merge '{mergeId}'.");

            if (string.Equals(next, mergeId, StringComparison.OrdinalIgnoreCase))
                return;

            cur = next;
        }
    }

    /// <summary>Sequential Router→PersonaCall edges in stable edge-id order (LLM candidate list).</summary>
    private static IReadOnlyList<ScenarioFlowRouterPersonaCandidate> ListRouterPersonaCandidates(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, ScenarioFlowNode> map,
        string routerId)
    {
        var list = new List<ScenarioFlowRouterPersonaCandidate>();
        foreach (var e in OutgoingSequentialEdges(flow, routerId))
        {
            var tid = e.ToNodeId.Trim();
            if (!map.TryGetValue(tid, out var n))
                continue;
            if (!string.Equals(n.Type, "PersonaCall", StringComparison.OrdinalIgnoreCase))
                continue;
            var pid = TryGetPersonaId(n.Config);
            if (string.IsNullOrWhiteSpace(pid))
                continue;
            list.Add(new ScenarioFlowRouterPersonaCandidate(tid, pid.Trim(), n.Label));
        }

        return list;
    }

    /// <summary>Maps model output order to graph node ids (duplicate personaIds dequeue in candidate edge order).</summary>
    private static List<string> MapPersonaPicksToNodeIds(
        IReadOnlyList<ScenarioFlowRouterPersonaCandidate> candidates,
        IReadOnlyList<string> personaOrder)
    {
        var qBy = new Dictionary<string, Queue<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            if (!qBy.TryGetValue(c.PersonaId, out var q))
            {
                q = new Queue<string>();
                qBy[c.PersonaId] = q;
            }

            q.Enqueue(c.NodeId);
        }

        var nodes = new List<string>();
        foreach (var p in personaOrder)
        {
            if (string.IsNullOrWhiteSpace(p))
                continue;
            var key = p.Trim();
            if (qBy.TryGetValue(key, out var queue) && queue.Count > 0)
                nodes.Add(queue.Dequeue());
        }

        return nodes;
    }

    /// <summary>Single LLM pick: run sequentially until <c>Output</c> (no shared Merge required).</summary>
    private static async Task<string> RunLinearBranchUntilOutputAsync(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, ScenarioFlowNode> map,
        Dictionary<string, string> store,
        HashSet<string> completed,
        string branchStart,
        string userMessage,
        PersonaInvoker invokePersona,
        TimeSpan personaTimeout,
        CancellationToken cancellationToken,
        IScenarioFlowExecutionObserver? observer = null)
    {
        var cur = branchStart;
        var cap = (flow.Nodes?.Count ?? 0) * 4 + 32;
        for (var i = 0; i < cap; i++)
        {
            await EnsureProcessedAsync(flow, map, store, completed, cur, userMessage, invokePersona, personaTimeout, cancellationToken, observer)
                .ConfigureAwait(false);
            if (!map.TryGetValue(cur, out var n))
                throw new ScenarioFlowExecutionException($"Unknown node '{cur}'.");

            if (string.Equals(n.Type, "Output", StringComparison.OrdinalIgnoreCase))
                return cur;

            if (OutgoingParallelEdges(flow, cur).Count >= 2)
                throw new ScenarioFlowExecutionException($"Nested parallel fan-out from '{cur}' is not supported.");

            var next = UniqueOutgoingSingleEdgeOrThrow(flow, cur);
            if (string.IsNullOrEmpty(next))
                throw new ScenarioFlowExecutionException($"Branch from '{branchStart}' did not reach Output.");

            cur = next;
        }

        throw new ScenarioFlowExecutionException("Branch execution exceeded step limit.");
    }

    /// <summary>Exactly one outgoing edge (any mode); used inside parallel branches toward Merge.</summary>
    private static string UniqueOutgoingSingleEdgeOrThrow(ScenarioFlowDocument flow, string fromId)
    {
        var outs = (flow.Edges ?? new List<ScenarioFlowEdge>())
            .Where(e => string.Equals(e.FromNodeId, fromId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (outs.Count == 0)
            return "";
        if (outs.Count > 1)
            throw new ScenarioFlowExecutionException($"Node '{fromId}' has {outs.Count} outgoing edges inside a branch (expected one).");

        return outs[0].ToNodeId.Trim();
    }

    /// <summary>Merge reachable via DFS (all edge modes) from each branch start; intersection must be exactly one Merge.</summary>
    private static string? FindCommonMergeId(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, ScenarioFlowNode> map,
        IReadOnlyList<string> branchStarts)
    {
        HashSet<string>? intersection = null;
        foreach (var start in branchStarts)
        {
            // Follow all edge modes so branch starts joined only by parallel links still reach the shared Merge.
            var reach = ReachableAnyMode(flow, start);
            var merges = new HashSet<string>(
                reach.Where(id => map.TryGetValue(id, out var n) && string.Equals(n.Type, "Merge", StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
            intersection = intersection == null
                ? merges
                : intersection.Intersect(merges, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (intersection.Count == 0)
                return null;
        }

        if (intersection == null || intersection.Count != 1)
            return null;

        return intersection.First();
    }

    private static HashSet<string> ReachableAnyMode(ScenarioFlowDocument flow, string start)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(start.Trim());
        while (stack.Count > 0)
        {
            var u = stack.Pop();
            if (!seen.Add(u))
                continue;
            foreach (var e in flow.Edges ?? new List<ScenarioFlowEdge>())
            {
                if (!string.Equals(e.FromNodeId, u, StringComparison.OrdinalIgnoreCase))
                    continue;
                stack.Push(e.ToNodeId.Trim());
            }
        }

        return seen;
    }

    private static List<ScenarioFlowEdge> OutgoingParallelEdges(ScenarioFlowDocument flow, string fromId) =>
        (flow.Edges ?? new List<ScenarioFlowEdge>())
            .Where(e => IsParallel(e) && string.Equals(e.FromNodeId, fromId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

    private static List<ScenarioFlowEdge> OutgoingSequentialEdges(ScenarioFlowDocument flow, string fromId) =>
        (flow.Edges ?? new List<ScenarioFlowEdge>())
            .Where(e => IsSequential(e) && string.Equals(e.FromNodeId, fromId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

    private static string GetIncomingText(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, string> store,
        string toNodeId,
        string userMessage)
    {
        var preds = IncomingAllModes(flow, toNodeId).ToList();
        if (preds.Count == 0)
            return userMessage;

        if (preds.Count != 1)
            throw new ScenarioFlowExecutionException($"Node '{toNodeId}' expected one predecessor for prompt text, found {preds.Count}.");

        var from = preds[0];
        if (store.TryGetValue(from, out var v) && v.Length > 0)
            return v;
        return userMessage;
    }

    private static IEnumerable<string> IncomingAllModes(ScenarioFlowDocument flow, string toNodeId)
    {
        foreach (var e in flow.Edges ?? new List<ScenarioFlowEdge>())
        {
            if (string.Equals(e.ToNodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
                yield return e.FromNodeId.Trim();
        }
    }

    private static string CombineIncoming(ScenarioFlowDocument flow, IReadOnlyDictionary<string, string> store, string toNodeId)
    {
        var parts = (flow.Edges ?? new List<ScenarioFlowEdge>())
            .Where(e => (IsSequential(e) || IsParallel(e)) && string.Equals(e.ToNodeId, toNodeId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Id, StringComparer.Ordinal)
            .Select(e => store.TryGetValue(e.FromNodeId.Trim(), out var s) ? s : "")
            .Where(s => s.Length > 0)
            .ToList();

        return parts.Count == 0 ? "" : string.Join("\n\n", parts);
    }

    /// <summary>Text stored on the Router node after <see cref="EnsureProcessedAsync"/> (upstream chain) or the original user message.</summary>
    private static string RouterInputText(
        ScenarioFlowDocument flow,
        IReadOnlyDictionary<string, string> store,
        string routerNodeId,
        string userMessage) =>
        store.TryGetValue(routerNodeId, out var t) && t.Length > 0 ? t : userMessage;

    private static string PickRouterTarget(ScenarioFlowDocument flow, string routerId, string userMessage)
    {
        var outs = OutgoingSequentialEdges(flow, routerId);
        if (outs.Count == 0)
            throw new ScenarioFlowExecutionException($"Router '{routerId}' has no outgoing sequential edges.");

        foreach (var e in outs)
        {
            var c = e.Condition?.Trim();
            if (string.IsNullOrEmpty(c)) continue;
            if (userMessage.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0)
                return e.ToNodeId.Trim();
        }

        var defaults = outs.Where(e => string.IsNullOrWhiteSpace(e.Condition)).ToList();
        if (defaults.Count == 1)
            return defaults[0].ToNodeId.Trim();
        if (defaults.Count == 0)
            throw new ScenarioFlowExecutionException($"Router '{routerId}' had no matching branch and no default edge.");

        throw new ScenarioFlowExecutionException($"Router '{routerId}' has multiple default (empty condition) edges.");
    }

    private static string UniqueOutgoingSequentialOrThrow(ScenarioFlowDocument flow, string fromId)
    {
        var outs = OutgoingSequentialEdges(flow, fromId);
        if (outs.Count == 0)
            return "";
        if (outs.Count > 1)
            throw new ScenarioFlowExecutionException($"Node '{fromId}' has {outs.Count} outgoing sequential edges (expected one).");

        return outs[0].ToNodeId.Trim();
    }

    private static bool IsSequential(ScenarioFlowEdge e) =>
        string.IsNullOrWhiteSpace(e.Mode) || string.Equals(e.Mode, "sequential", StringComparison.OrdinalIgnoreCase);

    private static bool IsParallel(ScenarioFlowEdge e) =>
        string.Equals(e.Mode, "parallel", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetPersonaId(JsonElement? config)
    {
        if (config is not { ValueKind: JsonValueKind.Object } el) return null;
        return el.TryGetProperty("personaId", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }
}
