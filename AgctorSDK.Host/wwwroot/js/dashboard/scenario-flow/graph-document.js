/**
 * PRD-014 + PRD-024: portable GraphDocument helpers (no Cytoscape).
 */
(function (global) {
    var nodeTypesV1 = ['ChatInput', 'Router', 'LlmNode', 'Merge', 'Output'];
    var nodeTypesV2 = ['Gate', 'WaitForInput', 'AwaitEvent', 'Notify'];
    var nodeTypes = nodeTypesV1.concat(nodeTypesV2);
    var edgeModes = ['sequential', 'parallel', 'loopBack'];
    var outputPolicies = ['first_non_empty', 'merge_sections', 'ranked'];
    var conditionMatchKinds = ['contains', 'equals', 'startsWith', 'endsWith', 'regex'];
    var gateOperators = ['isTrue', 'isFalse', 'equals', 'gt', 'lt'];
    var storeInvalidationPolicies = ['fromTargetForward', 'keepAll', 'iterationScopeOnly'];
    var knownFacts = [
        'visual.hasPhotos',
        'visual.extract.pending',
        'inbox.hasPending',
        'inbox.confirmed',
        'user.hasAttachments'
    ];
    var knownEvents = [
        'visual.extract.completed',
        'visual.extract.failed',
        'inbox.confirmed'
    ];

    function nodeById(nodes, id) {
        return (nodes || []).filter(function (n) { return n && n.id === id; })[0] || null;
    }

    function outgoingEdges(edges, fromId, mode) {
        return (edges || []).filter(function (e) {
            if (!e || e.fromNodeId !== fromId) return false;
            if (!mode) return true;
            return String(e.mode || 'sequential') === mode;
        });
    }

    /** True when graph needs PRD-024 runtime actor. */
    function isV2Flow(doc) {
        if (!doc) return false;
        if (String(doc.schemaVersion || '').trim() === '2.0') return true;
        var nodes = doc.nodes || [];
        var edges = doc.edges || [];
        if (nodes.some(function (n) { return n && nodeTypesV2.indexOf(n.type) >= 0; })) return true;
        return edges.some(function (e) { return e && String(e.mode || '') === 'loopBack'; });
    }

    /** Bump schema when v2 constructs present. */
    function normalizeSchemaVersion(doc) {
        if (!doc) return doc;
        if (isV2Flow(doc)) doc.schemaVersion = '2.0';
        else if (!doc.schemaVersion) doc.schemaVersion = '1.0';
        return doc;
    }

    function defaultConfigForNodeType(type) {
        switch (type) {
            case 'Gate':
                return {
                    fact: 'visual.hasPhotos',
                    operator: 'isFalse',
                    trueEdgeId: '',
                    falseEdgeId: ''
                };
            case 'WaitForInput':
                return {
                    promptTemplate: 'Please provide more information (you can upload photos).',
                    acceptAttachments: true,
                    attachmentPolicy: 'imagesOnly'
                };
            case 'AwaitEvent':
                return {
                    eventType: 'visual.extract.completed',
                    timeoutSeconds: 120,
                    timeoutEdgeId: ''
                };
            case 'Notify':
                return {
                    target: 'persona:style-coach',
                    signal: 'visual.photos.available',
                    includeStoreKeys: []
                };
            default:
                return {};
        }
    }

    function defaultLabelForNodeType(type) {
        switch (type) {
            case 'Gate': return 'Gate';
            case 'WaitForInput': return 'Ask user';
            case 'AwaitEvent': return 'Wait for event';
            case 'Notify': return 'Notify';
            default: return type;
        }
    }

    /** Short label for canvas edges; mirrors server routing fields. */
    function edgeRouteCaption(e) {
        e = e || {};
        var mode = String(e.mode || 'sequential');
        if (mode === 'loopBack') {
            var lc = e.loopConfig || {};
            var max = lc.maxAttempts != null ? lc.maxAttempts : '?';
            return 'loop:' + max;
        }
        var modeShort = mode.slice(0, 4);
        var cond = String(e.condition || '').trim();
        var cm = String(e.conditionMatch || 'contains').slice(0, 4);
        if (cond) {
            var t = cond.length > 16 ? cond.slice(0, 16) + '\u2026' : cond;
            return modeShort + ':' + cm + '\u00b7' + t;
        }
        if (e.llmRoutingHint && String(e.llmRoutingHint).trim()) return modeShort + ':llm';
        return modeShort + ':def';
    }

    function emptyFlow(scenarioId) {
        var sid = String(scenarioId || 'scenario').trim() || 'scenario';
        return {
            schemaVersion: '1.0',
            graphId: sid + '-flow',
            name: 'Flow',
            status: 'active',
            outputPolicy: 'merge_sections',
            sessionLoopCap: 10,
            nodes: [
                { id: 'in1', type: 'ChatInput', label: 'Chat input', config: {} },
                { id: 'out1', type: 'Output', label: 'Output', config: {} }
            ],
            edges: [{ id: 'e1', fromNodeId: 'in1', toNodeId: 'out1', mode: 'sequential' }],
            ui: { nodeLayouts: {} }
        };
    }

    function rosterHasId(roster, personaId) {
        var p = String(personaId || '').trim().toLowerCase();
        if (!p) return false;
        return roster.some(function (r) { return String(r || '').trim().toLowerCase() === p; });
    }

    function validateLoopRegions(edges, errors) {
        var byRegion = {};
        (edges || []).forEach(function (e) {
            if (!e || String(e.mode || '') !== 'loopBack') return;
            var lc = e.loopConfig || {};
            if (!lc.maxAttempts || lc.maxAttempts < 1) {
                errors.push('LOOP_MISSING_MAX_ATTEMPTS: Loop back edge "' + e.id + '" requires max attempts.');
            }
            if (!lc.loopRegionId) {
                errors.push('Loop back edge "' + e.id + '" requires loopRegionId.');
            } else {
                if (!byRegion[lc.loopRegionId]) byRegion[lc.loopRegionId] = lc.maxAttempts;
                else if (byRegion[lc.loopRegionId] !== lc.maxAttempts) {
                    errors.push('REGION_ATTEMPT_MISMATCH: Loop region "' + lc.loopRegionId + '" has conflicting max attempts.');
                }
            }
            if (!lc.storeInvalidation || storeInvalidationPolicies.indexOf(lc.storeInvalidation) < 0) {
                errors.push('Loop back edge "' + e.id + '" needs storeInvalidation.');
            }
        });
    }

    function validateV2Nodes(nodes, edges, errors) {
        (nodes || []).forEach(function (n) {
            if (!n) return;
            if (n.type === 'Gate') {
                var g = n.config || {};
                if (!g.fact) errors.push('GATE_MISSING_BRANCH: Gate "' + (n.label || n.id) + '" (' + n.id + ') needs fact.');
                if (!g.trueEdgeId || !g.falseEdgeId) {
                    errors.push('GATE_MISSING_BRANCH: Gate "' + (n.label || n.id) + '" (' + n.id + ') must define true and false branch edge ids.');
                }
            }
            if (n.type === 'WaitForInput') {
                var cfg = n.config || {};
                if (!String(cfg.promptTemplate || '').trim()) {
                    errors.push('Ask user node "' + (n.label || n.id) + '" (' + n.id + ') needs a prompt.');
                }
                var out = outgoingEdges(edges, n.id);
                if (!out.length) {
                    errors.push('SUSPEND_NO_RESUME: Ask user "' + (n.label || n.id) + '" (' + n.id + ') has no resume or loop back path.');
                }
            }
            if (n.type === 'AwaitEvent') {
                var ev = n.config || {};
                if (!ev.eventType) errors.push('Wait for event "' + (n.label || n.id) + '" (' + n.id + ') needs eventType.');
            }
        });
    }

    /** Client-side structural checks; API returns authoritative errors. */
    function validateFlowDocument(doc, opts) {
        opts = opts || {};
        var roster = Array.isArray(opts.personaAgentIds) ? opts.personaAgentIds : null;
        var errors = [];
        if (!doc || typeof doc !== 'object') {
            return { ok: false, errors: ['Flow document is missing.'] };
        }
        normalizeSchemaVersion(doc);
        if (!doc.schemaVersion) errors.push('schemaVersion is required.');
        if (!doc.graphId) errors.push('graphId is required.');
        if (outputPolicies.indexOf(doc.outputPolicy) < 0) errors.push('Invalid outputPolicy.');
        var nodes = Array.isArray(doc.nodes) ? doc.nodes : [];
        var edges = Array.isArray(doc.edges) ? doc.edges : [];
        if (!nodes.length) errors.push('At least one node is required.');
        var ids = {};
        var inputs = 0;
        var outputs = 0;
        var llmNodeCount = 0;
        nodes.forEach(function (n) {
            if (!n || !n.id) {
                errors.push('Each node needs an id.');
                return;
            }
            if (ids[n.id]) errors.push('Duplicate node id: ' + n.id);
            ids[n.id] = true;
            if (nodeTypes.indexOf(n.type) < 0) errors.push('Unknown node type for ' + n.id + ': ' + n.type);
            if (n.type === 'ChatInput') inputs++;
            if (n.type === 'Output') outputs++;
            if (n.type === 'LlmNode') {
                llmNodeCount++;
                var cfg = n.config || {};
                if (!cfg.personaId) errors.push('LlmNode ' + n.id + ' needs config.personaId.');
                else if (roster && roster.length > 0 && !rosterHasId(roster, cfg.personaId)) {
                    errors.push('LlmNode ' + n.id + ': personaId "' + cfg.personaId + '" is not in this scenario\'s persona roster.');
                }
            }
        });
        if (llmNodeCount > 0 && roster && roster.length === 0) {
            errors.push('Flow has LlmNode nodes but this scenario has no YAML personas on the roster — add personas on the scenario form.');
        }
        if (inputs < 1) errors.push('Need at least one ChatInput node.');
        if (outputs < 1) errors.push('Need at least one Output node.');
        edges.forEach(function (e) {
            if (!e || !e.id) {
                errors.push('Each edge needs an id.');
                return;
            }
            if (!e.fromNodeId || !ids[e.fromNodeId]) errors.push('Edge ' + e.id + ' has invalid fromNodeId.');
            if (!e.toNodeId || !ids[e.toNodeId]) errors.push('Edge ' + e.id + ' has invalid toNodeId.');
            if (edgeModes.indexOf(e.mode || 'sequential') < 0) errors.push('Edge ' + e.id + ' has invalid mode.');
            if (e.conditionMatch && conditionMatchKinds.indexOf(String(e.conditionMatch).toLowerCase()) < 0) {
                errors.push('Edge ' + e.id + ': unknown conditionMatch (use contains, equals, startsWith, endsWith, regex).');
            }
            var fromN = nodeById(nodes, e.fromNodeId);
            if (fromN && fromN.type === 'Router') {
                var rc = fromN.config || {};
                if (String(rc.routerMode || '').toLowerCase() !== 'llm' && String(e.conditionMatch || '').toLowerCase() === 'regex' && String(e.condition || '').trim()) {
                    try {
                        new RegExp(String(e.condition).trim(), 'i');
                    } catch (err) {
                        errors.push('Edge ' + e.id + ': invalid regex condition.');
                    }
                }
            }
        });
        nodes.forEach(function (n) {
            if (!n || n.type !== 'Router') return;
            var cfg = n.config || {};
            if (String(cfg.routerMode || '').toLowerCase() === 'llm') return;
            var rid = n.id;
            var seqOut = outgoingEdges(edges, rid, 'sequential');
            var defc = seqOut.filter(function (e) { return !String(e.condition || '').trim(); }).length;
            if (defc > 1) {
                errors.push('Router "' + rid + '" (deterministic): at most one default (empty condition) edge; found ' + defc + '.');
            }
        });
        if (isV2Flow(doc)) {
            validateLoopRegions(edges, errors);
            validateV2Nodes(nodes, edges, errors);
        }
        return { ok: errors.length === 0, errors: errors };
    }

    /** Resolve resume target after WaitForInput / AwaitEvent (loopBack preferred). */
    function resolveResumeTargetNode(doc, suspendedNodeId) {
        var edges = doc.edges || [];
        var loop = edges.filter(function (e) {
            return e && e.fromNodeId === suspendedNodeId && String(e.mode || '') === 'loopBack';
        })[0];
        if (loop) return loop.toNodeId;
        var seq = edges.filter(function (e) {
            return e && e.fromNodeId === suspendedNodeId && (!e.mode || e.mode === 'sequential');
        })[0];
        if (seq) return seq.toNodeId;
        return null;
    }

    /** Gate branch: returns next node id or null. */
    function evaluateGate(doc, node, facts) {
        facts = facts || {};
        var g = node.config || {};
        var factKey = String(g.fact || '');
        var val = facts[factKey];
        var op = String(g.operator || 'isTrue');
        var pass = false;
        if (op === 'isTrue') pass = !!val;
        else if (op === 'isFalse') pass = !val;
        else if (op === 'equals') pass = String(val) === String(g.value != null ? g.value : '');
        var edgeId = pass ? g.trueEdgeId : g.falseEdgeId;
        var edge = (doc.edges || []).filter(function (e) { return e && e.id === edgeId; })[0];
        return edge ? edge.toNodeId : null;
    }

    /** Structural multi-turn simulate (no LLM). */
    function simulateTurn(doc, turnInput, prevState) {
        var v = validateFlowDocument(doc);
        if (!v.ok) return { ok: false, errors: v.errors, state: prevState };

        var nodes = doc.nodes || [];
        var state = prevState ? JSON.parse(JSON.stringify(prevState)) : {
            executionNodeId: '',
            status: 'Running',
            pendingPrompt: null,
            awaitingEvent: null,
            loopRegions: {},
            facts: {},
            steps: [],
            completed: false,
            output: null
        };

        var startId = state.executionNodeId;
        if (!startId || state.status === 'Completed' || state.status === 'Idle') {
            var chat = nodes.filter(function (n) { return n && n.type === 'ChatInput'; })[0];
            if (!chat) return { ok: false, errors: ['No ChatInput node.'], state: state };
            startId = chat.id;
            state.status = 'Running';
        } else if (state.status === 'WaitingForUserInput') {
            var resume = resolveResumeTargetNode(doc, startId);
            if (!resume) return { ok: false, errors: ['Cannot resume from ' + startId], state: state };
            startId = resume;
            state.status = 'Running';
            state.pendingPrompt = null;
            if (turnInput && turnInput.attachments && turnInput.attachments.length) {
                state.facts['user.hasAttachments'] = true;
                state.facts['visual.hasPhotos'] = true;
            }
            if (turnInput && turnInput.message) {
                state.facts['inbox.confirmed'] = /confirm|yes|ok/i.test(String(turnInput.message));
            }
        } else if (state.status === 'WaitingForDomainEvent') {
            if (!turnInput || !turnInput.eventType) {
                return { ok: false, errors: ['Flow is waiting for domain event: ' + (state.awaitingEvent || 'unknown')], state: state };
            }
            var resumeEv = resolveResumeTargetNode(doc, startId);
            if (!resumeEv) return { ok: false, errors: ['Cannot resume after event from ' + startId], state: state };
            startId = resumeEv;
            state.status = 'Running';
            state.awaitingEvent = null;
            state.facts[turnInput.eventType] = true;
        }

        var current = startId;
        var guard = 0;
        var maxSteps = nodes.length * 4 + 16;

        while (guard++ < maxSteps) {
            var node = nodeById(nodes, current);
            if (!node) return { ok: false, errors: ['Unknown node ' + current], state: state };

            state.steps.push({ id: node.id, label: node.label || node.id, type: node.type });
            state.executionNodeId = node.id;

            if (node.type === 'Gate') {
                var nextGate = evaluateGate(doc, node, state.facts);
                if (!nextGate) return { ok: false, errors: ['Gate "' + node.id + '" branch unresolved.'], state: state };
                current = nextGate;
                continue;
            }
            if (node.type === 'WaitForInput') {
                state.status = 'WaitingForUserInput';
                state.pendingPrompt = (node.config && node.config.promptTemplate) || node.label || 'Waiting for input';
                return { ok: true, errors: [], state: state };
            }
            if (node.type === 'AwaitEvent') {
                state.status = 'WaitingForDomainEvent';
                state.awaitingEvent = (node.config && node.config.eventType) || 'unknown';
                return { ok: true, errors: [], state: state };
            }
            if (node.type === 'Notify') {
                var seqN = outgoingEdges(doc.edges, node.id, 'sequential')[0];
                if (!seqN) return { ok: false, errors: ['Notify "' + node.id + '" has no outgoing edge.'], state: state };
                current = seqN.toNodeId;
                continue;
            }
            if (node.type === 'Output') {
                state.status = 'Completed';
                state.completed = true;
                state.output = '(simulated output from Output node "' + node.id + '")';
                return { ok: true, errors: [], state: state };
            }

            var loopEdge = outgoingEdges(doc.edges, node.id, 'loopBack')[0];
            if (loopEdge) {
                var lc = loopEdge.loopConfig || {};
                var regionId = lc.loopRegionId || 'loop-' + node.id;
                if (!state.loopRegions[regionId]) state.loopRegions[regionId] = { attempt: 0, maxAttempts: lc.maxAttempts || 3 };
                state.loopRegions[regionId].attempt++;
                if (state.loopRegions[regionId].attempt > state.loopRegions[regionId].maxAttempts) {
                    return { ok: false, errors: ['Loop region "' + regionId + '" exceeded max attempts.'], state: state };
                }
                current = loopEdge.toNodeId;
                continue;
            }

            var seqOut = outgoingEdges(doc.edges, node.id, 'sequential');
            var parOut = outgoingEdges(doc.edges, node.id, 'parallel');
            if (parOut.length >= 2) {
                current = parOut[0].toNodeId;
                continue;
            }
            if (seqOut.length === 1) {
                current = seqOut[0].toNodeId;
                continue;
            }
            if (node.type === 'Router') {
                var rOut = seqOut[0];
                if (rOut) {
                    current = rOut.toNodeId;
                    continue;
                }
            }
            return { ok: false, errors: ['No outgoing edge from "' + node.id + '".'], state: state };
        }
        return { ok: false, errors: ['Simulate exceeded step limit.'], state: state };
    }

    /** v1 stub: list node types in traversal order from ChatInput (BFS). */
    function simulateOrder(doc) {
        var v = validateFlowDocument(doc);
        if (!v.ok) return { ok: false, errors: v.errors, order: [] };
        var nodes = doc.nodes || [];
        var edges = doc.edges || [];
        var adj = {};
        edges.forEach(function (e) {
            if (!e.fromNodeId || !e.toNodeId) return;
            if (!adj[e.fromNodeId]) adj[e.fromNodeId] = [];
            adj[e.fromNodeId].push(e.toNodeId);
        });
        var starts = nodes.filter(function (n) { return n.type === 'ChatInput'; }).map(function (n) { return n.id; });
        var order = [];
        var seen = {};
        var q = starts.slice();
        starts.forEach(function (s) { seen[s] = true; });
        while (q.length) {
            var u = q.shift();
            order.push(u);
            (adj[u] || []).forEach(function (v2) {
                if (!seen[v2]) {
                    seen[v2] = true;
                    q.push(v2);
                }
            });
        }
        var nodeByIdMap = {};
        nodes.forEach(function (n) {
            if (n && n.id) nodeByIdMap[n.id] = n;
        });
        var steps = order.map(function (id, idx) {
            var n = nodeByIdMap[id] || {};
            return {
                id: id,
                label: n.label || id,
                type: n.type || '',
                index: idx + 1
            };
        });
        return { ok: true, errors: [], order: order, steps: steps };
    }

    function defaultLoopConfig(fromNodeId) {
        return {
            loopRegionId: 'loop-' + String(fromNodeId || 'region'),
            maxAttempts: 3,
            storeInvalidation: 'fromTargetForward',
            incrementAttempt: true
        };
    }

    global.AgctorScenarioFlow = global.AgctorScenarioFlow || {};
    global.AgctorScenarioFlow.nodeTypes = nodeTypes;
    global.AgctorScenarioFlow.nodeTypesV2 = nodeTypesV2;
    global.AgctorScenarioFlow.knownFacts = knownFacts;
    global.AgctorScenarioFlow.knownEvents = knownEvents;
    global.AgctorScenarioFlow.gateOperators = gateOperators;
    global.AgctorScenarioFlow.storeInvalidationPolicies = storeInvalidationPolicies;
    global.AgctorScenarioFlow.isV2Flow = isV2Flow;
    global.AgctorScenarioFlow.normalizeSchemaVersion = normalizeSchemaVersion;
    global.AgctorScenarioFlow.defaultConfigForNodeType = defaultConfigForNodeType;
    global.AgctorScenarioFlow.defaultLabelForNodeType = defaultLabelForNodeType;
    global.AgctorScenarioFlow.defaultLoopConfig = defaultLoopConfig;
    global.AgctorScenarioFlow.emptyFlow = emptyFlow;
    global.AgctorScenarioFlow.validateFlowDocument = validateFlowDocument;
    global.AgctorScenarioFlow.simulateOrder = simulateOrder;
    global.AgctorScenarioFlow.simulateTurn = simulateTurn;
    global.AgctorScenarioFlow.edgeRouteCaption = edgeRouteCaption;
})(typeof window !== 'undefined' ? window : globalThis);
