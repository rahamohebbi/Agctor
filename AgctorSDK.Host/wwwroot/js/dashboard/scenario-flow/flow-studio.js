/** Presentation and JSON inspection for the canonical flow document. */
(function (global) {
    var context = null;
    var refreshFrame = null;
    var jsonVisible = false;
    function el(id) { return document.getElementById('sc-flow-' + id); }
    function typeLabel(type) {
        return { ChatInput: 'Input', LlmNode: 'AI step', WaitForInput: 'Ask user', AwaitEvent: 'Wait for event' }[type] || type;
    }
    function snapshot(flushInspectors) {
        if (!context) return null;
        return flushInspectors ? context.read() : context.renderer.read(context.base);
    }
    function showJson(doc) {
        if (!doc) return;
        var text = JSON.stringify(doc, null, 2);
        // Keep text selection and scroll intact when nothing changed.
        if (el('json-source').value !== text) el('json-source').value = text;
        el('json-filename').textContent = filename(doc);
    }
    function filename(doc) {
        return (String(doc.graphId || 'flow').replace(/[^a-z0-9._-]/gi, '_') || 'flow') + '.json';
    }
    function refresh() {
        refreshFrame = null;
        if (!context) return;
        var cy = context.renderer.getCy();
        if (!cy) return;
        el('counts').textContent = cy.nodes().length + ' steps · ' + cy.edges().length + ' connections';
        el('zoom-level').textContent = Math.round(cy.zoom() * 100) + '%';
        var selected = cy.$(':selected');
        var node = selected.length === 1 && selected[0].isNode() ? selected[0] : null;
        el('selection-title').textContent = node ? node.data('label') : selected.length === 1 ? 'Connection' : selected.length ? selected.length + ' selected' : 'Your flow';
        el('selection-meta').textContent = node ? typeLabel(node.data('agctorType')) + ' · ' + node.id() : selected.length === 1 ? selected[0].source().data('label') + ' → ' + selected[0].target().data('label') : selected.length ? 'Connect two steps, or select one item to edit.' : 'Select a step or connection to configure it.';
        var configurable = node && ['Router', 'LlmNode', 'Gate', 'WaitForInput', 'AwaitEvent', 'Notify'].indexOf(node.data('agctorType')) >= 0;
        el('selection-empty').classList.toggle('hidden', !!configurable || (selected.length === 1 && !node));
        if (node && !configurable) {
            el('selection-empty').querySelector('h4').textContent = typeLabel(node.data('agctorType'));
            el('selection-empty').querySelector('p').textContent = 'This step needs no extra configuration. Select two steps to connect it to your flow.';
        } else {
            el('selection-empty').querySelector('h4').textContent = 'Make it yours';
            el('selection-empty').querySelector('p').textContent = 'Choose a step on the canvas or in the list to edit its settings. Select a connection to set its routing rules.';
        }
        el('connect').disabled = cy.$('node:selected').length !== 2;
        el('connect-loop').disabled = cy.$('node:selected').length !== 2;
        el('delete-edges').disabled = !cy.$('edge:selected').length;
        el('delete-selection').disabled = !selected.length;
        el('delete-selection').textContent = node ? 'Delete node' : selected.length === 1 ? 'Delete connection' : 'Delete selected';
        var outline = el('outline');
        var existing = Array.from(outline.children);
        cy.nodes().forEach(function (n, index) {
            var button = existing[index];
            if (!button || button.dataset.nodeId !== n.id()) {
                button = document.createElement('button');
                button.type = 'button';
                button.dataset.nodeId = n.id();
                button.appendChild(document.createElement('span'));
                button.appendChild(document.createElement('small'));
                if (existing[index]) outline.replaceChild(button, existing[index]);
                else outline.appendChild(button);
            }
            button.firstChild.textContent = n.data('label');
            button.lastChild.textContent = typeLabel(n.data('agctorType'));
            button.setAttribute('aria-pressed', String(n.selected()));
        });
        while (outline.children.length > cy.nodes().length) outline.lastChild.remove();
        if (jsonVisible) showJson(snapshot(false));
    }
    function scheduleRefresh() {
        if (refreshFrame === null) refreshFrame = requestAnimationFrame(refresh);
    }
    function setView(json) {
        jsonVisible = json;
        el('view-graph').setAttribute('aria-pressed', String(!json));
        el('view-json').setAttribute('aria-pressed', String(json));
        el('workspace').classList.toggle('hidden', json);
        el('json-panel').classList.toggle('hidden', !json);
        el('viewport-tools').classList.toggle('hidden', json);
        if (!context) return;
        if (json) showJson(snapshot(true));
        else requestAnimationFrame(function () { if (context) context.renderer.getCy().resize(); });
    }
    function exportText() {
        var doc = snapshot(true);
        if (!doc) return null;
        showJson(doc);
        return { text: JSON.stringify(doc, null, 2), filename: filename(doc) };
    }
    el('view-graph').addEventListener('click', function () { setView(false); });
    el('view-json').addEventListener('click', function () { setView(true); });
    el('fit').addEventListener('click', function () { if (context) context.renderer.fitViewport(); });
    el('arrange').addEventListener('click', function () { if (context) context.renderer.autoLayout(); });
    ['in', 'out'].forEach(function (direction) {
        el('zoom-' + direction).addEventListener('click', function () {
            if (!context) return;
            var cy = context.renderer.getCy();
            cy.zoom({ level: cy.zoom() * (direction === 'in' ? 1.2 : 1 / 1.2), renderedPosition: { x: cy.width() / 2, y: cy.height() / 2 } });
        });
    });
    el('outline').addEventListener('click', function (event) {
        var button = event.target.closest('[data-node-id]');
        if (!button || !context) return;
        var cy = context.renderer.getCy();
        var node = cy.getElementById(button.dataset.nodeId);
        if (!event.shiftKey) cy.$(':selected').unselect();
        if (event.shiftKey && node.selected()) node.unselect();
        else node.select();
        var bounds = node.renderedBoundingBox();
        if (bounds.x1 < 0 || bounds.y1 < 0 || bounds.x2 > cy.width() || bounds.y2 > cy.height()) {
            cy.animate({ center: { eles: node }, duration: 180 });
        }
    });
    el('json-copy').addEventListener('click', async function () {
        var data = exportText();
        if (!data) return;
        try {
            await navigator.clipboard.writeText(data.text);
            el('json-status').textContent = 'JSON copied to clipboard.';
        } catch (error) {
            el('json-source').focus();
            el('json-source').select();
            el('json-status').textContent = 'Clipboard access is unavailable. The JSON is selected; use your keyboard to copy it.';
        }
    });
    el('json-download').addEventListener('click', function () {
        var data = exportText();
        if (!data) return;
        var url = URL.createObjectURL(new Blob([data.text + '\n'], { type: 'application/json' }));
        var anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = data.filename;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
        el('json-status').textContent = 'JSON download requested. Save the flow to store these changes in the scenario.';
    });
    global.AgctorScenarioFlow.studio = {
        mount: function (renderer, base, read, scenarioName) {
            context = { renderer: renderer, base: base, read: read };
            el('scenario-name').textContent = scenarioName || 'SCENARIO';
            el('json-status').textContent = 'Changes are stored in the scenario only after you save.';
            renderer.getCy().on('add remove data position select unselect zoom', scheduleRefresh);
            setView(false);
            refresh();
        },
        destroy: function () {
            if (context && context.renderer.getCy()) context.renderer.getCy().off('add remove data position select unselect zoom', scheduleRefresh);
            if (refreshFrame !== null) cancelAnimationFrame(refreshFrame);
            refreshFrame = null;
            context = null;
            setView(false);
            el('json-source').value = '';
            el('outline').replaceChildren();
        }
    };
})(window);
