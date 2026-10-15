(function (global) {
    var root = document.getElementById('pm-chat-model-settings');
    var activeKey = '', activeSession = null, state = null, revision = 0;
    var fields = root.querySelector('[data-model-fields]');
    var nodes = root.querySelector('[data-model-nodes]');
    var effective = root.querySelector('[data-model-effective]');
    var status = root.querySelector('[data-model-status]');
    var save = root.querySelector('[data-model-save]');
    var reset = root.querySelector('[data-model-reset]');
    var reload = root.querySelector('[data-model-reload]');
    var labels = global.AgctorModels.labels;
    var selectClass = 'w-full rounded-lg border border-gray-300 bg-white p-2 text-xs dark:border-gray-600 dark:bg-gray-800';
    function row(labelText, id) {
        var wrap = document.createElement('div');
        var label = document.createElement('label'); label.htmlFor = id; label.textContent = labelText;
        label.className = 'block text-xs font-medium mb-1';
        var select = document.createElement('select'); select.id = id; select.className = selectClass;
        wrap.append(label, select); return {wrap: wrap, select: select};
    }
    Object.keys(labels).forEach(function (role) { var r = row(labels[role], 'pm-model-' + role); r.select.dataset.role = role; fields.appendChild(r.wrap); });
    function busy(value) {
        save.disabled = reset.disabled = reload.disabled = value;
        root.querySelectorAll('select').forEach(function (s) { s.disabled = value; });
    }
    async function api(url, options) {
        var r = await fetch(url, options); var data = await r.json();
        if (!r.ok) throw new Error(data.error || 'Could not load model settings.');
        return data;
    }
    function render(data) {
        state = data;
        Object.keys(labels).forEach(function (role) {
            global.AgctorModels.populate(document.getElementById('pm-model-' + role), role, data.settings.defaults[role], '')
                .catch(function (e) { status.textContent = e.message; });
        });
        nodes.replaceChildren();
        (data.nodes || []).forEach(function (node, index) {
            var r = row((node.label || node.id) + ' · ' + labels[node.role], 'pm-model-node-' + index);
            r.select.dataset.node = node.id; r.select.dataset.role = node.role;
            var override = data.settings.nodes[node.id];
            global.AgctorModels.populate(r.select, node.role, override && override.role === node.role ? override.model : '', '')
                .catch(function (e) { status.textContent = e.message; });
            nodes.appendChild(r.wrap);
        });
        if (!(data.nodes || []).length) nodes.textContent = 'No model-driven flow nodes in this scenario.';
        effective.replaceChildren();
        function line(label, model) {
            var li = document.createElement('li');
            li.textContent = label + ': ' + (model.model || 'Not configured') + ' · ' + model.source;
            effective.appendChild(li);
        }
        Object.keys(labels).forEach(function (role) { line(labels[role], data.plan.defaults[role]); });
        (data.nodes || []).forEach(function (node) { if (data.plan.nodes[node.id]) line(node.label || node.id, data.plan.nodes[node.id]); });
    }
    async function openSession(id, scenario, force) {
        var key = (id || '') + ':' + (scenario || '');
        if (!force && key === activeKey) return;
        activeKey = key; activeSession = id; state = null;
        var token = ++revision; busy(true); status.textContent = id ? 'Loading model choices…' : 'Select a session first.';
        fields.querySelectorAll('select').forEach(function (s) { s.disabled = true; });
        nodes.replaceChildren(); effective.replaceChildren();
        if (!id) return;
        try {
            var data = await api('/api/model-settings/sessions/' + encodeURIComponent(id));
            if (revision !== token) return;
            render(data); status.textContent = 'Saved choices apply to the next message. Background events keep the current run’s choices.';
            fields.querySelectorAll('select').forEach(function (s) { s.disabled = false; });
            busy(false);
        } catch (e) { if (revision === token) { status.textContent = e.message; reload.disabled = false; } }
    }
    async function persist(clear) {
        if (!state || !activeSession) return;
        var session = activeSession, token = revision;
        var settings = { scenarioId: state.settings.scenarioId, defaults: {}, nodes: {} };
        if (!clear) {
            fields.querySelectorAll('select').forEach(function (s) { if (s.value) settings.defaults[s.dataset.role] = s.value; });
            nodes.querySelectorAll('select').forEach(function (s) { if (s.value) settings.nodes[s.dataset.node] = { role:s.dataset.role, model:s.value }; });
        }
        busy(true); status.textContent = 'Saving model choices…';
        try {
            var data = await api('/api/model-settings/sessions/' + encodeURIComponent(session), { method:'PUT', headers:{'Content-Type':'application/json'}, body:JSON.stringify(settings) });
            if (revision !== token) return;
            render(data); status.textContent = clear ? 'Chat overrides cleared. Scenario defaults and explicit node models apply.' : 'Saved for this chat. Applies to the next message.';
        } catch (e) { if (revision === token) status.textContent = e.message; }
        finally { if (revision === token) busy(false); }
    }
    save.addEventListener('click', function () { persist(false); });
    reset.addEventListener('click', function () { persist(true); });
    root.addEventListener('change', function (event) {
        if (event.target.matches('select')) status.textContent = 'Unsaved model choices. Save to apply them to the next message.';
    });
    reload.addEventListener('click', function () {
        global.AgctorModels.load(true).then(function () { return openSession(activeSession, state && state.settings.scenarioId, true); }).catch(function (e) { status.textContent = e.message; });
    });
    global.AgctorChatModels = { openSession:openSession };
})(window);
