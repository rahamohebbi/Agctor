(() => {
    'use strict';
    const el = id => document.getElementById(id);
    let workspace = null;
    let busy = false;
    let agents = [];
    const url = () => `/api/scene-memory/workspaces/${encodeURIComponent(workspace.id)}`;

    async function api(path, body) {
        const response = await fetch(path, body === undefined ? {} : {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
        });
        const data = await response.json();
        if (!response.ok) throw new Error(data.error || Object.values(data.errors || {}).flat().join(' ') || data.title || 'Request failed.');
        return data;
    }

    function availability() {
        el('sm-workspace').disabled = busy;
        el('sm-create-workspace').querySelector('button').disabled = busy;
        el('sm-entity-fields').disabled = busy || !workspace;
        el('sm-fact-fields').disabled = busy || !workspace?.entities.length;
        el('sm-direct-fields').disabled = busy || !workspace;
        el('sm-create-agent-fields').disabled = busy || !workspace;
        el('sm-agent-fields').disabled = busy || !agents.length;
    }

    async function run(action) {
        if (busy) return;
        busy = true;
        el('sm-message').textContent = '';
        availability();
        try { await action(); }
        catch (error) { el('sm-message').textContent = error.message; }
        finally { busy = false; availability(); }
    }

    function options(id, items, blank) {
        const select = el(id), previous = select.value;
        select.replaceChildren();
        if (blank) select.add(new Option(blank, ''));
        items.forEach(item => select.add(new Option(item.name, item.id)));
        if ([...select.options].some(option => option.value === previous)) select.value = previous;
    }

    function table(target, headings, rows, empty) {
        const container = el(target);
        container.replaceChildren();
        if (!rows.length) { container.textContent = empty; return; }
        const table = document.createElement('table');
        const header = table.createTHead().insertRow();
        headings.forEach(text => {
            const th = document.createElement('th'); th.scope = 'col'; th.textContent = text; header.append(th);
        });
        const body = table.createTBody();
        rows.forEach(values => {
            const row = body.insertRow(); values.forEach(value => { row.insertCell().textContent = value; });
        });
        container.append(table);
    }

    function render() {
        const entities = workspace?.entities || [], history = workspace?.history || [];
        options('sm-fact-entity', entities);
        options('sm-query-entity', entities, 'Use the question below');
        const rows = [];
        entities.forEach(entity => {
            const facts = new Map();
            history.filter(f => f.entityId === entity.id).forEach(f => facts.set(f.property, f));
            if (!facts.size) rows.push([entity.name, entity.kind, '—', 'No facts recorded', '—']);
            facts.forEach(f => rows.push([entity.name, entity.kind, f.property, f.value, `#${f.sequence}`]));
        });
        table('sm-current', ['Entity', 'Type', 'Property', 'Current value', 'Source change'], rows, 'Add an entity to begin.');
        table('sm-history', ['Change', 'Entity', 'Property', 'Value', 'Event note'], [...history].reverse().map(f =>
            [`#${f.sequence}`, entities.find(e => e.id === f.entityId)?.name || f.entityId, f.property, f.value, f.note || '—']), 'No changes recorded.');
        el('sm-history-title').textContent = `Change history (${history.length})`;
        document.querySelectorAll('.scene-memory input[name="atSequence"]').forEach(input => { input.max = history.length; });
        availability();
    }

    async function loadWorkspace(id) {
        workspace = null; agents = [];
        options('sm-agent', []);
        el('sm-direct-result').textContent = 'Results will appear here.';
        el('sm-agent-result').textContent = 'Agent → Scene Memory tool → saved facts';
        document.querySelectorAll('.scene-memory input[name="atSequence"]').forEach(input => { input.value = ''; });
        render();
        if (!id) return;
        workspace = await api(`/api/scene-memory/workspaces/${encodeURIComponent(id)}`);
        render();
        agents = await api(`${url()}/agents`);
        options('sm-agent', agents);
    }

    async function listWorkspaces(selected) {
        const list = await api('/api/scene-memory/workspaces');
        options('sm-workspace', list, list.length ? null : 'Create your first workspace');
        if (selected) el('sm-workspace').value = selected;
        await loadWorkspace(el('sm-workspace').value);
    }

    function form(id, action) {
        el(id).addEventListener('submit', event => {
            event.preventDefault();
            const data = Object.fromEntries(new FormData(event.currentTarget));
            run(() => action(data));
        });
    }

    function query(data) {
        return { entityId: data.entityId || null, question: data.question || null,
            atSequence: data.atSequence ? Number(data.atSequence) : null };
    }

    function result(id, data, prefix = '') {
        const sources = data.facts.map(f => `#${f.sequence} · ${f.property}: ${f.value}${f.note ? ` (${f.note})` : ''}`);
        el(id).textContent = `${prefix}${data.answer}\nAs of change #${data.atSequence}` +
            (sources.length ? `\n\nSource changes\n${sources.join('\n')}` : '');
    }

    el('sm-workspace').addEventListener('change', () => run(() => loadWorkspace(el('sm-workspace').value)));
    form('sm-create-workspace', async data => {
        const created = await api('/api/scene-memory/workspaces', data);
        el('sm-create-workspace').reset();
        await listWorkspaces(created.id);
    });
    form('sm-add-entity', async data => {
        workspace = await api(`${url()}/entities`, data);
        el('sm-add-entity').reset(); render();
        el('sm-fact-entity').value = workspace.entities.at(-1).id;
    });
    form('sm-record-fact', async data => {
        workspace = await api(`${url()}/facts`, data); render();
        el('sm-direct-result').textContent = 'Memory changed. Query again to see the latest facts.';
        el('sm-agent-result').textContent = 'Memory changed. Ask the agent again to see the latest facts.';
    });
    form('sm-direct-query', async data => result('sm-direct-result', await api(`${url()}/query`, query(data))));
    form('sm-create-agent', async data => {
        const agent = await api(`${url()}/agents`, data);
        agents = await api(`${url()}/agents`);
        options('sm-agent', agents); el('sm-agent').value = agent.id;
        el('sm-create-agent').reset();
        el('sm-agent-result').textContent = `${agent.name} is ready. Ask about an entity in this workspace.`;
    });
    form('sm-agent-query', async data => {
        const response = await api(`${url()}/agents/${encodeURIComponent(data.agentId)}/query`, query(data));
        result('sm-agent-result', response.result, 'Agent → Scene Memory tool → saved facts\n\n');
    });
    run(() => listWorkspaces());
})();
