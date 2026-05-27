/**
 * PRD-014: portable GraphDocument helpers (no Cytoscape). Server still validates on save.
 */
(function (global) {
    var nodeTypes = ['ChatInput', 'Router', 'LlmNode', 'Merge', 'Output'];
    var edgeModes = ['sequential', 'parallel'];
    var outputPolicies = ['first_non_empty', 'merge_sections', 'ranked'];
    var conditionMatchKinds = ['contains', 'equals', 'startsWith', 'endsWith', 'regex'];

    /** Short label for canvas edges; mirrors server routing fields. */
    function edgeRouteCaption(e) {
        e = e || {};
        var mode = String(e.mode || 'sequ').slice(0, 4);
        var cond = String(e.condition || '').trim();
        var cm = String(e.conditionMatch || 'contains').slice(0, 4);
        if (cond) {
            var t = cond.length > 16 ? cond.slice(0, 16) + '\u2026' : cond;
            return mode + ':' + cm + '\u00b7' + t;
        }
        if (e.llmRoutingHint && String(e.llmRoutingHint).trim()) return mode + ':llm';
        return mode + ':def';
    }

    function emptyFlow(scenarioId) {
        var sid = String(scenarioId || 'scenario').trim() || 'scenario';
        return {
            schemaVersion: '1.0',
            graphId: sid + '-flow',
            name: 'Flow',
            status: 'active',
            outputPolicy: 'merge_sections',
            nodes: [
                { id: 'in1', type: 'ChatInput', label: 'Chat input', config: {} },
                { id: 'out1', type: 'Output', label: 'Output', config: {} }
            ],
            edges: [{ id: 'e1', fromNodeId: 'in1', toNodeId: 'out1', mode: 'sequential' }],
            ui: { nodeLayouts: {} }
        };
    }

    /** True if roster contains id (case-insensitive). */
    function rosterHasId(roster, personaId) {
        var p = String(personaId || '').trim().toLowerCase();
        if (!p) return false;
        return roster.some(function (r) { return String(r || '').trim().toLowerCase() === p; });
    }

    /** Client-side structural checks; API returns authoritative errors.
     * @param {object} [opts] — optional `personaAgentIds` (string[]) for LlmNode roster check (PRD-014 Phase 11). */
    function validateFlowDocument(doc, opts) {
        opts = opts || {};
        var roster = Array.isArray(opts.personaAgentIds) ? opts.personaAgentIds : null;
        var errors = [];
        if (!doc || typeof doc !== 'object') {
            return { ok: false, errors: ['Flow document is missing.'] };
        }
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
            if (edgeModes.indexOf(e.mode) < 0) errors.push('Edge ' + e.id + ' has invalid mode.');
            if (e.conditionMatch && conditionMatchKinds.indexOf(String(e.conditionMatch).toLowerCase()) < 0) {
                errors.push('Edge ' + e.id + ': unknown conditionMatch (use contains, equals, startsWith, endsWith, regex).');
            }
            var fromN = nodes.filter(function (n) { return n && n.id === e.fromNodeId; })[0];
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
            var seqOut = edges.filter(function (e) {
                return e && e.fromNodeId === rid && (!e.mode || e.mode === 'sequential');
            });
            var defc = seqOut.filter(function (e) { return !String(e.condition || '').trim(); }).length;
            if (defc > 1) {
                errors.push('Router "' + rid + '" (deterministic): at most one default (empty condition) edge; found ' + defc + '.');
            }
        });
        return { ok: errors.length === 0, errors: errors };
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
            (adj[u] || []).forEach(function (v) {
                if (!seen[v]) {
                    seen[v] = true;
                    q.push(v);
                }
            });
        }
        var nodeById = {};
        nodes.forEach(function (n) {
            if (n && n.id) nodeById[n.id] = n;
        });
        var steps = order.map(function (id, idx) {
            var n = nodeById[id] || {};
            return {
                id: id,
                label: n.label || id,
                type: n.type || '',
                index: idx + 1
            };
        });
        return { ok: true, errors: [], order: order, steps: steps };
    }

    global.AgctorScenarioFlow = global.AgctorScenarioFlow || {};
    global.AgctorScenarioFlow.emptyFlow = emptyFlow;
    global.AgctorScenarioFlow.validateFlowDocument = validateFlowDocument;
    global.AgctorScenarioFlow.simulateOrder = simulateOrder;
    global.AgctorScenarioFlow.edgeRouteCaption = edgeRouteCaption;
})(typeof window !== 'undefined' ? window : globalThis);
