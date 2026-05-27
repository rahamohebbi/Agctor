/**
 * PRD-014: Cytoscape-only adapter. Canonical graph is GraphDocument; this maps to/from cy elements.
 */
(function (global) {
    function configToString(config) {
        try {
            return JSON.stringify(config && typeof config === 'object' ? config : {});
        } catch (e) {
            return '{}';
        }
    }

    function parseConfig(s) {
        try {
            var o = JSON.parse(s || '{}');
            return typeof o === 'object' && o !== null ? o : {};
        } catch (e) {
            return {};
        }
    }

    /** Model-space point at the center of the current canvas (stays on-screen after pan/zoom). */
    function modelAtViewportCenter(cy) {
        var w = cy.width();
        var h = cy.height();
        if (!w || !h) return { x: 80, y: 120 };
        var pan = cy.pan();
        var zoom = cy.zoom();
        if (!isFinite(zoom) || zoom === 0) zoom = 1;
        // Inverse of model→rendered: rendered = model * zoom + pan (container-local pixels).
        return {
            x: (w * 0.5 - pan.x) / zoom,
            y: (h * 0.5 - pan.y) / zoom
        };
    }

    /**
     * Pick source/target for a new edge between two nodes when both are selected.
     * Uses pipeline order ChatInput → Router → LlmNode → Merge → Output so
     * e.g. LlmNode–Output is always LlmNode → Output regardless of click order.
     */
    function inferConnectEndpoints(nodeA, nodeB) {
        var rank = { ChatInput: 0, Router: 1, LlmNode: 2, Merge: 3, Output: 4 };
        var ta = String(nodeA.data('agctorType') || '').trim();
        var tb = String(nodeB.data('agctorType') || '').trim();
        var ra = Object.prototype.hasOwnProperty.call(rank, ta) ? rank[ta] : null;
        var rb = Object.prototype.hasOwnProperty.call(rank, tb) ? rank[tb] : null;
        if (ra !== null && rb !== null && ra !== rb) {
            if (ra < rb) return { from: nodeA.id(), to: nodeB.id() };
            return { from: nodeB.id(), to: nodeA.id() };
        }
        return { from: nodeA.id(), to: nodeB.id() };
    }

    function CytoscapeRenderer() {
        this._cy = null;
        this._container = null;
        this._changeCb = null;
        /** Keeps canvas in sync when the modal panel is CSS-resized. */
        this._resizeObs = null;
    }

    CytoscapeRenderer.prototype.mount = function (container, doc) {
        this.destroy();
        this._container = container;
        if (!global.cytoscape) throw new Error('cytoscape is not loaded');
        var elements = graphToElements(doc);
        this._cy = global.cytoscape({
            container: container,
            elements: elements,
            style: [
                {
                    selector: 'node',
                    style: {
                        label: 'data(label)',
                        'text-valign': 'center',
                        'text-halign': 'center',
                        'background-color': '#6366f1',
                        color: '#fff',
                        width: 'label',
                        height: 'label',
                        padding: '10px',
                        'font-size': '11px',
                        'border-width': 2,
                        'border-color': 'rgba(255,255,255,0.35)',
                        'border-opacity': 1
                    }
                },
                {
                    selector: 'node[agctorType = "ChatInput"]',
                    style: { 'background-color': '#059669' }
                },
                {
                    selector: 'node[agctorType = "Output"]',
                    style: { 'background-color': '#b45309' }
                },
                {
                    selector: 'node[agctorType = "LlmNode"]',
                    style: { 'background-color': '#0284c7' }
                },
                {
                    selector: 'node[agctorType = "Router"]',
                    style: { 'background-color': '#7c3aed' }
                },
                {
                    selector: 'node[agctorType = "Merge"]',
                    style: { 'background-color': '#db2777' }
                },
                {
                    selector: 'edge',
                    style: {
                        width: 2,
                        'line-color': '#94a3b8',
                        'target-arrow-color': '#94a3b8',
                        'target-arrow-shape': 'triangle',
                        'curve-style': 'bezier',
                        label: 'data(routeCaption)',
                        'font-size': '8px',
                        'font-weight': '500',
                        color: '#64748b',
                        'text-background-color': '#f1f5f9',
                        'text-background-opacity': 0.92,
                        'text-background-padding': '1px 3px',
                        'text-background-shape': 'roundrectangle'
                    }
                },
                {
                    selector: 'node:selected',
                    style: {
                        'border-width': 5,
                        'border-color': '#facc15',
                        'border-opacity': 1,
                        'background-blacken': -0.15,
                        'z-index': 9999
                    }
                },
                {
                    selector: 'node.traverseHover',
                    style: {
                        'border-width': 6,
                        'border-color': '#22d3ee',
                        'border-opacity': 1,
                        'background-blacken': -0.2,
                        'z-index': 10000
                    }
                },
                {
                    selector: 'edge:selected',
                    style: {
                        width: 4,
                        'line-color': '#fbbf24',
                        'target-arrow-color': '#fbbf24',
                        'font-size': '8px',
                        color: '#475569',
                        'text-background-color': '#fffbeb'
                    }
                }
            ],
            layout: { name: 'breadthfirst', directed: true, spacingFactor: 1.45 },
            userZoomingEnabled: true,
            userPanningEnabled: true,
            boxSelectionEnabled: true
        });
        applyLayouts(this._cy, doc);
        var self = this;
        this._cy.on('add remove move dragfreeon position', function () {
            if (typeof self._changeCb === 'function') self._changeCb();
        });
        installAltDragEdgeRewire(this._cy, function () {
            if (typeof self._changeCb === 'function') self._changeCb();
        });
        if (typeof ResizeObserver !== 'undefined' && container) {
            this._resizeObs = new ResizeObserver(function () {
                if (!self._cy) return;
                try {
                    self._cy.resize();
                } catch (e) {
                    /* ignore */
                }
            });
            this._resizeObs.observe(container);
        }
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                self.fitViewport();
            });
        });
    };

    /** Resize graph to container pixel box and zoom/pan so all elements are visible (call after modal is visible). */
    CytoscapeRenderer.prototype.fitViewport = function () {
        if (!this._cy) return;
        try {
            this._cy.resize();
            this._cy.fit(undefined, 56);
        } catch (e) {
            /* ignore */
        }
    };

    /** Nearest edge end in rendered space (which endpoint user is grabbing). */
    function nearerEdgeEnd(edge, rx, ry) {
        var sp = edge.source().renderedPosition();
        var tp = edge.target().renderedPosition();
        var ds = (sp.x - rx) * (sp.x - rx) + (sp.y - ry) * (sp.y - ry);
        var dt = (tp.x - rx) * (tp.x - rx) + (tp.y - ry) * (tp.y - ry);
        return ds <= dt ? 'source' : 'target';
    }

    /** Node under rendered point (shallow hit: bounding box). */
    function nodeAtRenderedPoint(cy, rx, ry) {
        var found = null;
        cy.nodes().forEach(function (n) {
            var bb = n.renderedBoundingBox();
            if (rx >= bb.x1 && rx <= bb.x2 && ry >= bb.y1 && ry <= bb.y2) found = n;
        });
        return found;
    }

    /** Pointer position in container pixel space (matches `renderedBoundingBox`). */
    function clientToContainerLocal(cy, clientX, clientY) {
        var rect = cy.container().getBoundingClientRect();
        return { x: clientX - rect.left, y: clientY - rect.top };
    }

    /**
     * Alt + drag from a selected edge, release on a node to reattach that end (source or target).
     * Uses core `edge.move()` — no extra libraries.
     */
    function installAltDragEdgeRewire(cy, notifyChange) {
        var state = null;

        function endDrag() {
            if (!state) return;
            window.removeEventListener('mousemove', onMove, true);
            window.removeEventListener('mouseup', onUp, true);
            try {
                cy.panningEnabled(true);
                cy.boxSelectionEnabled(true);
            } catch (e) { /* ignore */ }
            state = null;
        }

        function onMove(e) {
            if (!state) return;
            e.preventDefault();
        }

        function onUp(e) {
            if (!state) return;
            try {
                var rp = clientToContainerLocal(cy, e.clientX, e.clientY);
                var node = nodeAtRenderedPoint(cy, rp.x, rp.y);
                var edge = state.edge;
                if (node && edge) {
                    var nid = node.id();
                    var sid = edge.source().id();
                    var tid = edge.target().id();
                    if (state.end === 'source') {
                        if (nid !== tid) edge.move({ source: nid });
                    } else {
                        if (nid !== sid) edge.move({ target: nid });
                    }
                }
            } catch (err) {
                /* ignore */
            }
            endDrag();
            if (typeof notifyChange === 'function') notifyChange();
        }

        cy.on('mousedown', 'edge', function (evt) {
            if (!evt.originalEvent || !evt.originalEvent.altKey) return;
            if (evt.originalEvent.button !== 0) return;
            var edge = evt.target;
            var sel = cy.$('edge:selected');
            if (sel.length !== 1 || sel[0].id() !== edge.id()) return;
            var rp = evt.renderedPosition || evt.cyRenderedPosition;
            if (!rp || typeof rp.x !== 'number') {
                if (!evt.originalEvent) return;
                rp = clientToContainerLocal(cy, evt.originalEvent.clientX, evt.originalEvent.clientY);
            }
            state = { edge: edge, end: nearerEdgeEnd(edge, rp.x, rp.y) };
            try {
                cy.panningEnabled(false);
                cy.boxSelectionEnabled(false);
            } catch (e2) { /* ignore */ }
            window.addEventListener('mousemove', onMove, true);
            window.addEventListener('mouseup', onUp, true);
            evt.preventDefault();
            evt.stopPropagation();
        });
    }

    function applyLayouts(cy, doc) {
        var layouts = (doc && doc.ui && doc.ui.nodeLayouts) || {};
        cy.nodes().forEach(function (n) {
            var p = layouts[n.id()];
            if (p && typeof p.x === 'number' && typeof p.y === 'number') {
                n.position({ x: p.x, y: p.y });
            }
        });
    }

    function graphToElements(doc) {
        var out = [];
        if (!doc || !Array.isArray(doc.nodes)) return out;
        doc.nodes.forEach(function (n) {
            out.push({
                group: 'nodes',
                data: {
                    id: n.id,
                    label: n.label || n.id,
                    agctorType: n.type,
                    agctorConfig: configToString(n.config)
                }
            });
        });
        (doc.edges || []).forEach(function (e) {
            var mode = e.mode || 'sequential';
            var condition = e.condition || '';
            var conditionMatch = e.conditionMatch || 'contains';
            var llmRoutingHint = e.llmRoutingHint || '';
            var cap =
                global.AgctorScenarioFlow && typeof global.AgctorScenarioFlow.edgeRouteCaption === 'function'
                    ? global.AgctorScenarioFlow.edgeRouteCaption({
                          mode: mode,
                          condition: condition,
                          conditionMatch: conditionMatch,
                          llmRoutingHint: llmRoutingHint
                      })
                    : mode;
            out.push({
                group: 'edges',
                data: {
                    id: e.id,
                    source: e.fromNodeId,
                    target: e.toNodeId,
                    mode: mode,
                    condition: condition,
                    conditionMatch: conditionMatch,
                    llmRoutingHint: llmRoutingHint,
                    routeCaption: cap
                }
            });
        });
        return out;
    }

    CytoscapeRenderer.prototype.read = function (baseDoc) {
        // Before mount (or after destroy) _cy is null — must not return an emptied graph or "save" wipes the flow.
        if (!this._cy) {
            return baseDoc ? JSON.parse(JSON.stringify(baseDoc)) : {};
        }
        var doc = baseDoc ? JSON.parse(JSON.stringify(baseDoc)) : {};
        doc.schemaVersion = doc.schemaVersion || '1.0';
        doc.graphId = doc.graphId || 'flow';
        doc.outputPolicy = doc.outputPolicy || 'merge_sections';
        doc.nodes = [];
        doc.edges = [];
        doc.ui = doc.ui || {};
        doc.ui.nodeLayouts = {};
        this._cy.nodes().forEach(function (n) {
            var cfg = parseConfig(n.data('agctorConfig'));
            doc.nodes.push({
                id: n.id(),
                type: n.data('agctorType') || 'Router',
                label: n.data('label') || n.id(),
                config: cfg
            });
            var pos = n.position();
            doc.ui.nodeLayouts[n.id()] = { x: pos.x, y: pos.y };
        });
        this._cy.edges().forEach(function (e) {
            var row = {
                id: e.id(),
                fromNodeId: e.data('source'),
                toNodeId: e.data('target'),
                mode: e.data('mode') || 'sequential',
                condition: e.data('condition') || undefined
            };
            var cm = e.data('conditionMatch');
            if (cm && String(cm).trim() && String(cm).toLowerCase() !== 'contains') row.conditionMatch = String(cm).trim();
            var hint = e.data('llmRoutingHint');
            if (hint && String(hint).trim()) row.llmRoutingHint = String(hint).trim();
            doc.edges.push(row);
        });
        return doc;
    };

    CytoscapeRenderer.prototype.onChange = function (cb) {
        this._changeCb = typeof cb === 'function' ? cb : null;
    };

    CytoscapeRenderer.prototype.addNode = function (type, label, config) {
        if (!this._cy) return null;
        var id = 'n_' + Math.random().toString(36).slice(2, 10);
        var cfg = config && typeof config === 'object' ? config : {};
        // Avoid graph-wide layout (would move every node). Place in the *visible* viewport — model coords at
        // bbox.x2 were often off-screen after pan/zoom.
        var pos = modelAtViewportCenter(this._cy);
        // Tiny jitter so rapid adds do not stack in one pixel.
        pos.x += (Math.random() - 0.5) * 24;
        pos.y += (Math.random() - 0.5) * 24;
        this._cy.add({
            group: 'nodes',
            data: {
                id: id,
                label: label || type,
                agctorType: type,
                agctorConfig: configToString(cfg)
            },
            position: pos
        });
        if (typeof this._changeCb === 'function') this._changeCb();
        return id;
    };

    CytoscapeRenderer.prototype.connect = function (fromId, toId, mode) {
        if (!this._cy || !fromId || !toId) return;
        var eid = 'e_' + Math.random().toString(36).slice(2, 10);
        var edgeMode = mode || 'sequential';
        var cap =
            global.AgctorScenarioFlow && typeof global.AgctorScenarioFlow.edgeRouteCaption === 'function'
                ? global.AgctorScenarioFlow.edgeRouteCaption({
                      mode: edgeMode,
                      condition: '',
                      conditionMatch: 'contains',
                      llmRoutingHint: ''
                  })
                : edgeMode;
        this._cy.add({
            group: 'edges',
            data: {
                id: eid,
                source: fromId,
                target: toId,
                mode: edgeMode,
                condition: '',
                conditionMatch: 'contains',
                llmRoutingHint: '',
                routeCaption: cap
            }
        });
        // Do not fitViewport here — keeps pan/zoom stable when wiring edges (same rationale as addNode).
        if (typeof this._changeCb === 'function') this._changeCb();
        return eid;
    };

    /** Connects two selected nodes; direction follows flow types when both are known, else first → second. */
    CytoscapeRenderer.prototype.connectSelected = function (mode) {
        if (!this._cy) return false;
        var sel = this._cy.$('node:selected');
        if (sel.length !== 2) return false;
        var ends = inferConnectEndpoints(sel[0], sel[1]);
        this.connect(ends.from, ends.to, mode || 'sequential');
        return true;
    };

    /** Removes selected edges only (nodes unchanged). Returns count removed. */
    CytoscapeRenderer.prototype.removeSelectedEdges = function () {
        if (!this._cy) return 0;
        var edges = this._cy.$('edge:selected');
        var n = edges.length;
        if (n > 0) edges.remove();
        return n;
    };

    CytoscapeRenderer.prototype.getCy = function () {
        return this._cy;
    };

    /** Highlights one node during “Simulate order” hover linking (does not change selection). */
    CytoscapeRenderer.prototype.setTraverseHighlight = function (nodeId) {
        if (!this._cy) return;
        this._cy.nodes().removeClass('traverseHover');
        if (!nodeId) return;
        var n = this._cy.getElementById(String(nodeId));
        if (n && n.nonempty()) n.addClass('traverseHover');
    };

    CytoscapeRenderer.prototype.clearTraverseHighlight = function () {
        if (!this._cy) return;
        this._cy.nodes().removeClass('traverseHover');
    };

    CytoscapeRenderer.prototype.destroy = function () {
        if (this._resizeObs) {
            try {
                this._resizeObs.disconnect();
            } catch (e) {
                /* ignore */
            }
            this._resizeObs = null;
        }
        if (this._cy) {
            this._cy.destroy();
            this._cy = null;
        }
        this._container = null;
        this._changeCb = null;
    };

    global.AgctorScenarioFlow = global.AgctorScenarioFlow || {};
    global.AgctorScenarioFlow.createCytoscapeRenderer = function () {
        return new CytoscapeRenderer();
    };
})(typeof window !== 'undefined' ? window : globalThis);
