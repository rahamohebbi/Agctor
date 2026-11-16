/**
 * Phase D: edit shared scene-video topology (fixed roster + layout + audio enable).
 * Saves via PUT /api/scenarios/{id}/topology; live graph reads the same document.
 */
(function (global) {
    'use strict';

    var NODE_W = 140;
    var NODE_H = 56;
    var PAD = 28;

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function shortLabel(id) {
        if (id === 'Scene Memory agent') return 'Memory agent';
        if (id === 'Render agent') return 'Render';
        if (id === 'Project agent') return 'Project';
        if (id === 'Editor agent') return 'Editor';
        if (id === 'Human') return 'You';
        return id;
    }

    function defaultLayouts(nodes, width, height) {
        var columns = { human: [], agent: [], other: [] };
        (nodes || []).forEach(function (n) {
            if (!n || !n.id) return;
            if (n.kind === 'human' || n.id === 'Director') columns.human.push(n.id);
            else if (n.kind === 'agent') columns.agent.push(n.id);
            else columns.other.push(n.id);
        });
        Object.keys(columns).forEach(function (k) { columns[k].sort(); });
        var used = ['human', 'agent', 'other'].map(function (k) { return columns[k]; }).filter(function (c) { return c.length; });
        var layouts = {};
        var w = Math.max(width || 720, used.length * 200);
        var h = Math.max(height || 360, Math.max.apply(null, used.map(function (c) { return c.length; }).concat([1])) * 90 + 40);
        used.forEach(function (col, i) {
            col.forEach(function (id, j) {
                layouts[id] = {
                    x: ((i + 0.5) * w) / used.length,
                    y: ((j + 0.5) * h) / col.length
                };
            });
        });
        return { layouts: layouts, width: w, height: h };
    }

    function mount(host, scenarioId, opts) {
        opts = opts || {};
        if (!host || !scenarioId) return null;

        var root = document.createElement('div');
        root.className = 'svt-editor';
        root.innerHTML =
            '<div class="svt-toolbar">' +
            '  <label class="svt-audio"><input type="checkbox" class="svt-audio-toggle" /> Audio Engineer enabled</label>' +
            '  <span class="svt-hint">Drag nodes to set the shared live layout. Roster and ownership stay locked.</span>' +
            '  <button type="button" class="sc-button sc-small svt-reset">Reset layout</button>' +
            '  <button type="button" class="sc-button sc-small sc-primary svt-save">Save topology</button>' +
            '</div>' +
            '<div class="svt-canvas" tabindex="0" role="application" aria-label="Agent topology editor">' +
            '  <svg class="svt-edges" aria-hidden="true"></svg>' +
            '  <div class="svt-nodes"></div>' +
            '</div>' +
            '<p class="svt-status" role="status"></p>';
        host.replaceChildren(root);

        var audioEl = root.querySelector('.svt-audio-toggle');
        var canvas = root.querySelector('.svt-canvas');
        var edgesSvg = root.querySelector('.svt-edges');
        var nodesEl = root.querySelector('.svt-nodes');
        var statusEl = root.querySelector('.svt-status');
        var saveBtn = root.querySelector('.svt-save');
        var resetBtn = root.querySelector('.svt-reset');

        var state = {
            scenarioId: scenarioId,
            doc: null,
            layouts: {},
            dirty: false,
            drag: null
        };

        function setStatus(msg, isError) {
            statusEl.textContent = msg || '';
            statusEl.classList.toggle('svt-error', !!isError);
        }

        function markDirty() {
            state.dirty = true;
            setStatus('Unsaved topology changes.');
        }

        function contentSize() {
            var maxX = 0, maxY = 0;
            Object.keys(state.layouts).forEach(function (id) {
                var p = state.layouts[id];
                maxX = Math.max(maxX, p.x);
                maxY = Math.max(maxY, p.y);
            });
            return {
                width: Math.max(canvas.clientWidth || 720, maxX + NODE_W / 2 + PAD),
                height: Math.max(320, maxY + NODE_H / 2 + PAD)
            };
        }

        function drawEdges() {
            var size = contentSize();
            edgesSvg.setAttribute('width', String(size.width));
            edgesSvg.setAttribute('height', String(size.height));
            canvas.style.minHeight = size.height + 'px';
            var parts = [];
            (state.doc.edges || []).forEach(function (e) {
                var a = state.layouts[e.from];
                var b = state.layouts[e.to];
                if (!a || !b) return;
                var midX = (a.x + b.x) / 2;
                var midY = (a.y + b.y) / 2 - 24;
                parts.push(
                    '<path d="M ' + a.x + ' ' + a.y + ' Q ' + midX + ' ' + midY + ' ' + b.x + ' ' + b.y +
                    '" fill="none" stroke="rgba(100,120,140,0.55)" stroke-width="1.4" stroke-dasharray="5 6"/>'
                );
            });
            edgesSvg.innerHTML = parts.join('');
        }

        function renderNodes() {
            nodesEl.replaceChildren();
            (state.doc.nodes || []).forEach(function (n) {
                if (!n || !n.id || n.kind === 'model') return;
                var pos = state.layouts[n.id];
                if (!pos) return;
                var el = document.createElement('button');
                el.type = 'button';
                el.className = 'svt-node svt-kind-' + (n.kind || 'agent');
                el.style.left = (pos.x - NODE_W / 2) + 'px';
                el.style.top = (pos.y - NODE_H / 2) + 'px';
                el.dataset.id = n.id;
                el.innerHTML =
                    '<span class="svt-kind">' + esc(String(n.kind || 'agent').toUpperCase()) + '</span>' +
                    '<strong>' + esc(shortLabel(n.label || n.id)) + '</strong>';
                el.title = n.id + (n.ownerAgentId ? ' · owned by ' + n.ownerAgentId : '');
                el.addEventListener('pointerdown', function (ev) {
                    if (ev.button !== 0) return;
                    ev.preventDefault();
                    el.setPointerCapture(ev.pointerId);
                    state.drag = {
                        id: n.id,
                        startX: ev.clientX,
                        startY: ev.clientY,
                        originX: pos.x,
                        originY: pos.y
                    };
                });
                nodesEl.appendChild(el);
            });
            drawEdges();
        }

        function applyDoc(doc) {
            state.doc = doc;
            audioEl.checked = doc.audioEnabled !== false;
            var saved = (doc.ui && doc.ui.nodeLayouts) || {};
            var seeded = defaultLayouts(doc.nodes, canvas.clientWidth || 720, 360);
            state.layouts = {};
            (doc.nodes || []).forEach(function (n) {
                if (!n || !n.id || n.kind === 'model') return;
                var fromSaved = saved[n.id];
                state.layouts[n.id] = fromSaved
                    ? { x: Number(fromSaved.x) || seeded.layouts[n.id].x, y: Number(fromSaved.y) || seeded.layouts[n.id].y }
                    : { x: seeded.layouts[n.id].x, y: seeded.layouts[n.id].y };
            });
            state.dirty = false;
            renderNodes();
            setStatus('');
        }

        canvas.addEventListener('pointermove', function (ev) {
            if (!state.drag) return;
            var dx = ev.clientX - state.drag.startX;
            var dy = ev.clientY - state.drag.startY;
            state.layouts[state.drag.id] = {
                x: Math.max(NODE_W / 2 + 8, state.drag.originX + dx),
                y: Math.max(NODE_H / 2 + 8, state.drag.originY + dy)
            };
            renderNodes();
            markDirty();
        });
        canvas.addEventListener('pointerup', function () { state.drag = null; });
        canvas.addEventListener('pointercancel', function () { state.drag = null; });

        audioEl.addEventListener('change', function () { markDirty(); });

        resetBtn.addEventListener('click', function () {
            if (!state.doc) return;
            var seeded = defaultLayouts(state.doc.nodes, canvas.clientWidth || 720, 360);
            state.layouts = seeded.layouts;
            renderNodes();
            markDirty();
        });

        saveBtn.addEventListener('click', async function () {
            if (!state.doc) return;
            saveBtn.disabled = true;
            setStatus('Saving…');
            try {
                var body = {
                    audioEnabled: !!audioEl.checked,
                    nodeLayouts: state.layouts
                };
                var response = await fetch('/api/scenarios/' + encodeURIComponent(state.scenarioId) + '/topology', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                });
                var data = await response.json().catch(function () { return {}; });
                if (!response.ok) {
                    var detail = Array.isArray(data.details) ? data.details.join(' ') : (data.message || 'Save failed.');
                    throw new Error(detail);
                }
                applyDoc(data);
                setStatus('Topology saved. Live graph uses this layout and audio flag.');
                if (typeof opts.onSaved === 'function') opts.onSaved(data);
            } catch (err) {
                setStatus(err.message || 'Could not save topology.', true);
            } finally {
                saveBtn.disabled = false;
            }
        });

        async function load() {
            setStatus('Loading topology…');
            try {
                var response = await fetch('/api/scenarios/' + encodeURIComponent(scenarioId) + '/topology');
                var data = await response.json().catch(function () { return null; });
                if (!response.ok || !data) throw new Error((data && data.message) || 'Topology not available.');
                applyDoc(data);
            } catch (err) {
                setStatus(err.message || 'Could not load topology.', true);
            }
        }

        load();
        return {
            reload: load,
            destroy: function () { host.replaceChildren(); }
        };
    }

    global.AgctorSceneVideoTopologyEditor = { mount: mount };
})(window);
