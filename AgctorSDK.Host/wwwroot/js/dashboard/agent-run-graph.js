/**
 * Live agent-run graph — scenario conversation-flow skeleton ∪ observed routes
 * from GET /api/agent-runs/{id}/graph. Flow is the only definition surface; activity overlays it.
 */
(function (global) {
    'use strict';

    var LABELS = {
        'Scene Memory agent': 'Memory agent',
        'Render agent': 'Render',
        'Project agent': 'Project',
        'Editor agent': 'Editor',
        'Continuity critic': 'Visual critic',
        'Visual critic': 'Visual critic',
        Human: 'You'
    };
    var COLORS = {
        llm: '#B6A3FF',
        model: '#B6A3FF',
        memory: '#FFBE79',
        tool: '#9CB7D3',
        human: '#64E7D2',
        agent: '#64E7D2'
    };
    var NODE_W = 170;
    var NODE_H = 100;
    var FLIGHT_MS = 1500;

    function label(id) {
        if (LABELS[id]) return LABELS[id];
        if (String(id).indexOf('Ollama · ') === 0) return 'Ollama';
        return id;
    }

    function color(kind) {
        return COLORS[kind] || COLORS.agent;
    }

    function kindLabel(kind) {
        if (kind === 'llm') return 'LLM';
        if (kind === 'model') return 'MODEL EVIDENCE';
        return String(kind || 'agent').toUpperCase();
    }

    function detailText(id, node) {
        var kind = (node && node.kind) || 'agent';
        if (kind === 'llm') return (node && node.detail) || String(id).replace(/^Ollama · /, '');
        if (kind === 'model') return 'Reported by Cynelo';
        if (kind === 'agent' || kind === 'human') return kind === 'human' ? 'human' : 'Actor mailbox';
        return (node && node.detail) || kind;
    }

    function edgeKey(from, to) {
        return [from, to].sort().join('\n');
    }

    function buildTopology(snapshot, events) {
        var nodes = {};
        var edges = {};
        (snapshot && snapshot.nodes || []).forEach(function (n) {
            if (n && n.id) nodes[n.id] = { id: n.id, kind: n.kind || 'agent', detail: n.detail };
        });
        (snapshot && snapshot.edges || []).forEach(function (e) {
            if (!e || !e.from || !e.to) return;
            edges[edgeKey(e.from, e.to)] = e;
        });
        (events || []).forEach(function (ev) {
            var route = ev && ev.route;
            if (!route || !route.from || !route.to || route.from === route.to) return;
            ['from', 'to'].forEach(function (side) {
                var id = route[side];
                var kindKey = side + 'Kind';
                var detailKey = side + 'Detail';
                nodes[id] = {
                    id: id,
                    kind: id === 'Human' ? 'human' : (route[kindKey] || (nodes[id] && nodes[id].kind) || 'agent'),
                    detail: route[detailKey] || (nodes[id] && nodes[id].detail)
                };
            });
            edges[edgeKey(route.from, route.to)] = route;
        });
        return { nodes: nodes, edges: edges };
    }

    function columnIndex(id, kind) {
        if (id === 'Director' || kind === 'human') return 0;
        if (kind === 'agent') return 1;
        if (kind === 'model') return 3;
        return 2;
    }

    function layoutColumns(topology, width, height) {
        var columns = [[], [], [], []];
        Object.keys(topology.nodes).forEach(function (id) {
            columns[columnIndex(id, topology.nodes[id].kind)].push(id);
        });
        columns.forEach(function (c) { c.sort(); });
        var used = columns.filter(function (c) { return c.length; });
        var positions = {};
        var contentW = Math.max(width, used.length * 205);
        var contentH = Math.max(height, columns.reduce(function (m, c) { return Math.max(m, c.length); }, 1) * 124 + 36);
        used.forEach(function (col, i) {
            col.forEach(function (id, j) {
                positions[id] = {
                    x: ((i + 0.5) * contentW) / used.length,
                    y: ((j + 0.5) * contentH) / col.length
                };
            });
        });
        return { positions: positions, width: contentW, height: contentH };
    }

    /** Prefer shared ui.nodeLayouts from the topology definition; fall back to columns. */
    function layout(topology, width, height, savedLayouts) {
        var ids = Object.keys(topology.nodes);
        var usable = 0;
        if (savedLayouts) {
            ids.forEach(function (id) {
                var p = savedLayouts[id];
                if (p && isFinite(Number(p.x)) && isFinite(Number(p.y))) usable += 1;
            });
        }
        if (!usable) return layoutColumns(topology, width, height);

        var fallback = layoutColumns(topology, width, height);
        var positions = {};
        var maxX = 0;
        var maxY = 0;
        ids.forEach(function (id) {
            var p = savedLayouts[id];
            if (p && isFinite(Number(p.x)) && isFinite(Number(p.y))) {
                positions[id] = { x: Number(p.x), y: Number(p.y) };
            } else {
                positions[id] = fallback.positions[id];
            }
            maxX = Math.max(maxX, positions[id].x);
            maxY = Math.max(maxY, positions[id].y);
        });
        return {
            positions: positions,
            width: Math.max(width, maxX + NODE_W / 2 + 36),
            height: Math.max(height, maxY + NODE_H / 2 + 36)
        };
    }

    function create(host, options) {
        options = options || {};
        var root = document.createElement('div');
        root.className = 'arg-root';
        root.innerHTML =
            '<div class="arg-toolbar">' +
            '  <label class="arg-focus-wrap"><span class="arg-sr">Focus</span>' +
            '    <select class="arg-focus" aria-label="Focus agent connections">' +
            '      <option value="">All observed connections</option>' +
            '    </select>' +
            '  </label>' +
            '  <div class="arg-zoom-controls">' +
            '    <button type="button" class="arg-fit" title="Fit graph">Fit graph</button>' +
            '    <button type="button" class="arg-zoom-out" aria-label="Zoom out">−</button>' +
            '    <span class="arg-zoom-label">100%</span>' +
            '    <button type="button" class="arg-zoom-in" aria-label="Zoom in">+</button>' +
            '  </div>' +
            '</div>' +
            '<div class="arg-viewport" tabindex="0" role="img" aria-label="Agent run graph">' +
            '  <div class="arg-world">' +
            '    <canvas class="arg-canvas"></canvas>' +
            '    <div class="arg-nodes"></div>' +
            '    <p class="arg-empty">Loading agent topology…</p>' +
            '  </div>' +
            '</div>' +
            '<p class="arg-legend">Highlighted nodes = pending work · Moving dots = messages · Drag to explore</p>';
        host.replaceChildren(root);

        var viewport = root.querySelector('.arg-viewport');
        var world = root.querySelector('.arg-world');
        var canvas = root.querySelector('.arg-canvas');
        var nodesEl = root.querySelector('.arg-nodes');
        var emptyEl = root.querySelector('.arg-empty');
        var focusEl = root.querySelector('.arg-focus');
        var zoomLabel = root.querySelector('.arg-zoom-label');
        var ctx = canvas.getContext('2d');

        var state = {
            snapshot: { nodes: [], edges: [], activeCalls: [] },
            events: [],
            flights: [],
            focus: '',
            scale: 1,
            panX: 0,
            panY: 0,
            contentW: 800,
            contentH: 480,
            dragging: false,
            lastX: 0,
            lastY: 0,
            fitted: false,
            clock: 0,
            raf: 0,
            destroyed: false
        };

        function visibleIds(topology) {
            var ids = Object.keys(topology.nodes);
            if (!state.focus) return ids;
            var set = {};
            set[state.focus] = true;
            Object.keys(topology.edges).forEach(function (key) {
                var e = topology.edges[key];
                if (e.from === state.focus) set[e.to] = true;
                if (e.to === state.focus) set[e.from] = true;
            });
            Object.keys(topology.edges).forEach(function (key) {
                var e = topology.edges[key];
                var toKind = topology.nodes[e.to] && topology.nodes[e.to].kind;
                if (set[e.from] && (toKind === 'model' || e.operation === 'Rendering dependency')) set[e.to] = true;
            });
            return Object.keys(set);
        }

        function applyTransform() {
            world.style.transform = 'translate(' + state.panX + 'px,' + state.panY + 'px) scale(' + state.scale + ')';
            zoomLabel.textContent = Math.round(state.scale * 100) + '%';
        }

        function fit() {
            var vw = viewport.clientWidth || 640;
            var vh = viewport.clientHeight || 420;
            var pad = 24;
            var sx = (vw - pad * 2) / Math.max(state.contentW, 1);
            var sy = (vh - pad * 2) / Math.max(state.contentH, 1);
            state.scale = Math.max(0.35, Math.min(1.4, Math.min(sx, sy)));
            state.panX = (vw - state.contentW * state.scale) / 2;
            state.panY = (vh - state.contentH * state.scale) / 2;
            state.fitted = true;
            applyTransform();
        }

        function pendingSet(now) {
            var set = {};
            (state.snapshot.activeCalls || []).forEach(function (c) {
                if (!c) return;
                var until = c.confirmedUntil ? Date.parse(c.confirmedUntil) : 0;
                if (until && until < now) return;
                set[c.from] = true;
                set[c.to] = true;
            });
            return set;
        }

        function draw() {
            if (state.destroyed) return;
            var topology = buildTopology(state.snapshot, state.events);
            var focusAgents = Object.keys(topology.nodes).filter(function (id) {
                return topology.nodes[id].kind === 'agent';
            }).sort();
            var selected = focusEl.value;
            focusEl.replaceChildren();
            var allOpt = document.createElement('option');
            allOpt.value = '';
            allOpt.textContent = 'All observed connections';
            focusEl.appendChild(allOpt);
            focusAgents.forEach(function (id) {
                var opt = document.createElement('option');
                opt.value = id;
                opt.textContent = label(id);
                focusEl.appendChild(opt);
            });
            if (selected && focusAgents.indexOf(selected) >= 0) focusEl.value = selected;
            else { focusEl.value = ''; state.focus = ''; }

            var keep = {};
            visibleIds(topology).forEach(function (id) { keep[id] = topology.nodes[id]; });
            var filtered = { nodes: keep, edges: {} };
            Object.keys(topology.edges).forEach(function (key) {
                var e = topology.edges[key];
                if (keep[e.from] && keep[e.to]) filtered.edges[key] = e;
            });

            // Prefer canonical layouts from the graph API (projected from flow.ui.nodeLayouts).
            var savedLayouts = (state.snapshot && state.snapshot.layouts)
                || (state.snapshot && state.snapshot.flow && state.snapshot.flow.ui && state.snapshot.flow.ui.nodeLayouts)
                || null;
            var laid = layout(filtered, viewport.clientWidth || 640, viewport.clientHeight || 420, savedLayouts);
            state.contentW = laid.width;
            state.contentH = laid.height;
            world.style.width = laid.width + 'px';
            world.style.height = laid.height + 'px';
            canvas.width = laid.width;
            canvas.height = laid.height;

            var now = Date.now();
            var pending = pendingSet(now);
            var active = {};
            Object.keys(pending).forEach(function (id) { active[id] = true; });
            state.flights = state.flights.filter(function (f) { return now - f.started < FLIGHT_MS; });
            state.flights.forEach(function (f) {
                if (f.from) active[f.from] = true;
                if (f.to) active[f.to] = true;
            });

            var nodeCount = Object.keys(laid.positions).length;
            emptyEl.hidden = nodeCount > 0;
            if (!nodeCount) {
                emptyEl.textContent = 'No flow graph yet. Edit the conversation flow, then start a sequence to watch live activity.';
            }
            nodesEl.replaceChildren();
            Object.keys(laid.positions).forEach(function (id) {
                var node = filtered.nodes[id];
                var kind = node.kind || 'agent';
                var pos = laid.positions[id];
                var el = document.createElement('div');
                el.className = 'arg-node' + (pending[id] || active[id] ? ' arg-node-hot' : '');
                el.style.left = (pos.x - NODE_W / 2) + 'px';
                el.style.top = (pos.y - NODE_H / 2) + 'px';
                el.style.borderColor = color(kind);
                el.dataset.kind = kind;
                var title = kind === 'llm' ? 'Ollama' : label(id);
                el.innerHTML =
                    '<span class="arg-kind">' + kindLabel(kind) + '</span>' +
                    '<strong class="arg-title">' + title + '</strong>' +
                    '<span class="arg-detail">' + detailText(id, node) + '</span>';
                if (pending[id]) {
                    var spin = document.createElement('span');
                    spin.className = 'arg-spin';
                    spin.setAttribute('aria-hidden', 'true');
                    el.querySelector('.arg-kind').prepend(spin);
                }
                nodesEl.appendChild(el);
            });

            ctx.clearRect(0, 0, canvas.width, canvas.height);
            ctx.lineWidth = 1.4;
            ctx.setLineDash([5, 6]);
            Object.keys(filtered.edges).forEach(function (key) {
                var e = filtered.edges[key];
                var a = laid.positions[e.from];
                var b = laid.positions[e.to];
                if (!a || !b) return;
                var midX = (a.x + b.x) / 2;
                var midY = (a.y + b.y) / 2 - 28;
                var stroke = color((filtered.nodes[e.to] && filtered.nodes[e.to].kind) || 'agent');
                ctx.strokeStyle = stroke + '99';
                ctx.beginPath();
                ctx.moveTo(a.x, a.y);
                ctx.quadraticCurveTo(midX, midY, b.x, b.y);
                ctx.stroke();
            });
            ctx.setLineDash([]);

            // Moving dots for recent flights (and soft pulse for pending edges).
            state.flights.forEach(function (f) {
                var a = laid.positions[f.from];
                var b = laid.positions[f.to];
                if (!a || !b) return;
                var t = Math.min(1, (now - f.started) / FLIGHT_MS);
                var midX = (a.x + b.x) / 2;
                var midY = (a.y + b.y) / 2 - 28;
                var x = (1 - t) * (1 - t) * a.x + 2 * (1 - t) * t * midX + t * t * b.x;
                var y = (1 - t) * (1 - t) * a.y + 2 * (1 - t) * t * midY + t * t * b.y;
                ctx.fillStyle = '#FFFFFF';
                ctx.beginPath();
                ctx.arc(x, y, 3.2, 0, Math.PI * 2);
                ctx.fill();
            });
            (state.snapshot.activeCalls || []).forEach(function (c) {
                var a = laid.positions[c.from];
                var b = laid.positions[c.to];
                if (!a || !b) return;
                var until = c.confirmedUntil ? Date.parse(c.confirmedUntil) : 0;
                if (until && until < now) return;
                var pulse = (Math.sin(now / 280) + 1) / 2;
                var t = 0.25 + pulse * 0.5;
                var midX = (a.x + b.x) / 2;
                var midY = (a.y + b.y) / 2 - 28;
                var x = (1 - t) * (1 - t) * a.x + 2 * (1 - t) * t * midX + t * t * b.x;
                var y = (1 - t) * (1 - t) * a.y + 2 * (1 - t) * t * midY + t * t * b.y;
                ctx.fillStyle = 'rgba(255,255,255,' + (0.45 + pulse * 0.45) + ')';
                ctx.beginPath();
                ctx.arc(x, y, 3.5, 0, Math.PI * 2);
                ctx.fill();
            });

            if (!state.fitted) fit();
            else applyTransform();
        }

        function tick() {
            if (state.destroyed) return;
            state.clock += 1;
            if (state.flights.length || (state.snapshot.activeCalls || []).length) draw();
            state.raf = requestAnimationFrame(tick);
        }

        function pushFlights(events) {
            var now = Date.now();
            (events || []).forEach(function (ev) {
                var route = ev && ev.route;
                if (!route || !route.from || !route.to || route.from === route.to) return;
                state.flights.push({ from: route.from, to: route.to, started: now });
            });
            while (state.flights.length > 40) state.flights.shift();
        }

        function setData(snapshot, events, opts) {
            opts = opts || {};
            state.snapshot = snapshot || { nodes: [], edges: [], activeCalls: [] };
            if (opts.replaceEvents) state.events = events || [];
            else if (events && events.length) {
                var known = {};
                state.events.forEach(function (e) { known[e.sequence] = true; });
                events.forEach(function (e) {
                    if (!known[e.sequence]) state.events.push(e);
                });
                pushFlights(events);
            }
            draw();
        }

        focusEl.addEventListener('change', function () {
            state.focus = focusEl.value;
            state.fitted = false;
            draw();
        });
        root.querySelector('.arg-fit').addEventListener('click', function () { fit(); });
        root.querySelector('.arg-zoom-in').addEventListener('click', function () {
            state.scale = Math.min(2.2, state.scale * 1.15);
            applyTransform();
        });
        root.querySelector('.arg-zoom-out').addEventListener('click', function () {
            state.scale = Math.max(0.3, state.scale / 1.15);
            applyTransform();
        });
        viewport.addEventListener('pointerdown', function (e) {
            if (e.target.closest('.arg-node')) return;
            state.dragging = true;
            state.lastX = e.clientX;
            state.lastY = e.clientY;
            viewport.setPointerCapture(e.pointerId);
        });
        viewport.addEventListener('pointermove', function (e) {
            if (!state.dragging) return;
            state.panX += e.clientX - state.lastX;
            state.panY += e.clientY - state.lastY;
            state.lastX = e.clientX;
            state.lastY = e.clientY;
            applyTransform();
        });
        viewport.addEventListener('pointerup', function () { state.dragging = false; });
        viewport.addEventListener('pointercancel', function () { state.dragging = false; });
        viewport.addEventListener('wheel', function (e) {
            e.preventDefault();
            var factor = e.deltaY < 0 ? 1.08 : 1 / 1.08;
            state.scale = Math.max(0.3, Math.min(2.2, state.scale * factor));
            applyTransform();
        }, { passive: false });

        window.addEventListener('resize', function onResize() {
            if (state.destroyed) return;
            state.fitted = false;
            draw();
        });

        draw();
        state.raf = requestAnimationFrame(tick);

        return {
            setData: setData,
            fit: fit,
            destroy: function () {
                state.destroyed = true;
                cancelAnimationFrame(state.raf);
                host.replaceChildren();
            }
        };
    }

    function isAudioLoop(node) {
        return node && node.type === 'AgentLoop' && node.config && node.config.role === 'audio-engineer';
    }
    function isDirectorLoop(node) {
        return node && node.type === 'AgentLoop' && !isAudioLoop(node);
    }
    function canonicalFlowId(node) {
        if (!node) return '';
        if (node.type === 'ChatInput') return 'Human';
        if (isAudioLoop(node)) return 'Audio Engineer';
        if (isDirectorLoop(node)) return 'Director';
        if (node.type === 'Output') return (node.label && String(node.label).trim()) || 'Output';
        return (node.label && String(node.label).trim()) || node.id;
    }
    function kindForFlowNode(node) {
        if (node.type === 'ChatInput') return 'human';
        if (node.type === 'Output') return 'tool';
        if (node.type === 'LlmNode') return 'llm';
        return 'agent';
    }

    /** Convert scenario conversation-flow JSON into a live-graph snapshot (skeleton only). */
    function flowToSnapshot(flow) {
        var nodes = [];
        var edges = [];
        var layouts = {};
        var map = {};
        (flow && flow.nodes || []).forEach(function (n) {
            if (!n || !n.id) return;
            var id = canonicalFlowId(n);
            map[n.id] = id;
            nodes.push({
                id: id,
                kind: kindForFlowNode(n),
                detail: n.type === 'AgentLoop' ? 'Actor mailbox' : (n.label || n.type)
            });
        });
        (flow && flow.edges || []).forEach(function (e) {
            if (!e || !map[e.fromNodeId] || !map[e.toNodeId]) return;
            edges.push({
                from: map[e.fromNodeId],
                to: map[e.toNodeId],
                operation: e.mode || 'sequential',
                outcome: 'defined',
                count: 0
            });
        });
        var rawLayouts = flow && flow.ui && flow.ui.nodeLayouts;
        if (rawLayouts) {
            Object.keys(rawLayouts).forEach(function (flowId) {
                if (!map[flowId]) return;
                layouts[map[flowId]] = rawLayouts[flowId];
            });
        }
        return { nodes: nodes, edges: edges, activeCalls: [], flow: flow || null, layouts: layouts };
    }

    /** @deprecated Use flowToSnapshot — kept for older callers that still pass topology docs. */
    function definitionToSnapshot(definition) {
        if (definition && definition.nodes && definition.nodes[0] && definition.nodes[0].type)
            return flowToSnapshot(definition);
        var nodes = (definition && definition.nodes || []).filter(function (n) { return n && n.id && n.kind !== 'model'; })
            .map(function (n) { return { id: n.id, kind: n.kind || 'agent', detail: n.detail }; });
        var edges = (definition && definition.edges || []).filter(function (e) { return e && e.from && e.to; })
            .map(function (e) { return { from: e.from, to: e.to, operation: e.intent || 'defined', outcome: 'defined', count: 0 }; });
        return { nodes: nodes, edges: edges, activeCalls: [], definition: definition || null };
    }

    /**
     * Polls graph + events for one scenario run and keeps a viewer updated.
     * opts.scenarioUrl — when set, empty/new runs show the conversation-flow skeleton.
     * opts.topologyUrl — legacy alias ignored when scenarioUrl is set.
     */
    function mountLive(host, getRunId, opts) {
        opts = opts || {};
        var viewer = create(host, opts);
        var cursor = 0;
        var busy = false;
        var timer = 0;
        var lastRun = null;
        var skeleton = null;
        var scenarioUrl = opts.scenarioUrl || null;

        async function api(url) {
            var response = await fetch(url);
            var data = await response.json().catch(function () { return {}; });
            if (!response.ok) throw new Error(data.error || data.detail || 'Could not load the agent graph.');
            return data;
        }

        async function ensureSkeleton() {
            if (skeleton) return skeleton;
            if (scenarioUrl) {
                var scenario = await api(scenarioUrl);
                skeleton = flowToSnapshot(scenario.flow || scenario.Flow || null);
                return skeleton;
            }
            if (opts.topologyUrl) {
                skeleton = definitionToSnapshot(await api(opts.topologyUrl));
                return skeleton;
            }
            return null;
        }

        async function poll() {
            if (busy) return;
            var id = typeof getRunId === 'function' ? getRunId() : getRunId;
            busy = true;
            try {
                if (!id) {
                    lastRun = null;
                    cursor = 0;
                    var def = await ensureSkeleton();
                    viewer.setData(def || { nodes: [], edges: [], activeCalls: [] }, [], { replaceEvents: true });
                    if (opts.onStatus) opts.onStatus(null);
                    return;
                }
                var reset = id !== lastRun;
                if (reset) { lastRun = id; cursor = 0; }
                var graph = await api('/api/agent-runs/' + encodeURIComponent(id) + '/graph');
                var events = await api('/api/agent-runs/' + encodeURIComponent(id) + '/events?after=' + cursor);
                // Server unions conversation-flow skeleton ∪ observed routes.
                viewer.setData(graph, events, { replaceEvents: reset || cursor === 0 });
                events.forEach(function (e) { cursor = Math.max(cursor, e.sequence || 0); });
                if (opts.onStatus) opts.onStatus(null);
            } catch (err) {
                if (opts.onStatus) opts.onStatus(err);
            } finally {
                busy = false;
            }
        }

        poll();
        timer = setInterval(poll, opts.intervalMs || 2000);
        var onVis = function () { if (!document.hidden) poll(); };
        document.addEventListener('visibilitychange', onVis);

        return {
            // Drop cached skeleton so flow edits show up after save/refresh.
            refresh: function () { skeleton = null; return poll(); },
            destroy: function () {
                clearInterval(timer);
                document.removeEventListener('visibilitychange', onVis);
                viewer.destroy();
            }
        };
    }

    global.AgctorAgentRunGraph = {
        create: create,
        mountLive: mountLive,
        label: label,
        buildTopology: buildTopology,
        flowToSnapshot: flowToSnapshot,
        definitionToSnapshot: definitionToSnapshot
    };
})(window);
