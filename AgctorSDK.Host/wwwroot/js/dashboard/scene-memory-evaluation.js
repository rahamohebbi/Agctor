(() => {
    'use strict';
    const el = id => document.getElementById(id);
    let catalog = null, job = null, timer = null;

    async function api(path, options) {
        const response = await fetch(path, options);
        const text = await response.text();
        const data = text ? JSON.parse(text) : {};
        if (!response.ok) throw new Error(data.error || data.message || data.title || 'Request failed.');
        return data;
    }
    function node(tag, text, className) { const n = document.createElement(tag); if (text !== undefined) n.textContent = text; if (className) n.className = className; return n; }
    function formatBytes(value) {
        const sign = value < 0 ? '−' : '+'; const n = Math.abs(value);
        if (n < 1024 * 1024) return `${sign}${Math.round(n / 1024)} KB`;
        return `${sign}${(n / 1024 / 1024).toFixed(1)} MB`;
    }
    function selectedModels() { return [...el('sme-models').selectedOptions].map(o => o.value); }

    async function loadModels() {
        el('sme-error').textContent = '';
        try {
            const data = await api('/api/Llm/models');
            const previous = new Set(selectedModels()); el('sme-models').replaceChildren();
            (data.models || []).forEach((model, index) => { const option = new Option(model.name, model.name); option.selected = previous.has(model.name) || (!previous.size && index === 0); el('sme-models').add(option); });
            if (!data.models?.length) el('sme-error').textContent = 'No installed Ollama models were found.';
        } catch (error) { el('sme-error').textContent = error.message; }
    }
    async function loadCatalog() {
        catalog = await api('/api/scene-memory/evaluations/catalog');
        el('sme-categories').replaceChildren();
        catalog.categories.forEach(category => {
            const label = node('label'); const input = document.createElement('input'); input.type = 'checkbox'; input.value = category.name; input.checked = true;
            label.append(input, document.createTextNode(`${category.name} (${category.count})`)); el('sme-categories').append(label);
        });
    }
    function setRunning(running) {
        el('sme-start').disabled = running; el('sme-refresh-models').disabled = running; el('sme-models').disabled = running; el('sme-limit').disabled = running;
        el('sme-cancel').hidden = !running; el('sme-categories').querySelectorAll('input').forEach(i => { i.disabled = running; });
    }
    async function start() {
        el('sme-error').textContent = '';
        const models = selectedModels();
        if (!models.length) { el('sme-error').textContent = 'Select at least one model.'; return; }
        if (models.length > 3) { el('sme-error').textContent = 'Select no more than three models.'; return; }
        const categories = [...el('sme-categories').querySelectorAll('input:checked')].map(i => i.value);
        if (!categories.length) { el('sme-error').textContent = 'Select at least one category.'; return; }
        try {
            setRunning(true);
            job = await api('/api/scene-memory/evaluations', { method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify({ models, categories, limit:Number(el('sme-limit').value) }) });
            el('sme-run-panel').hidden = false; el('sme-results-panel').hidden = false; el('sme-comparison-panel').hidden = false;
            render(); poll();
        } catch (error) { setRunning(false); el('sme-error').textContent = error.message; }
    }
    async function poll() {
        clearTimeout(timer);
        try { job = await api(`/api/scene-memory/evaluations/${encodeURIComponent(job.id)}`); render(); }
        catch (error) { el('sme-error').textContent = error.message; setRunning(false); return; }
        if (job.status === 'queued' || job.status === 'running') timer = setTimeout(poll, 1200); else setRunning(false);
    }
    function render() {
        const results = job.results || [], completed = job.completed || 0;
        el('sme-progress').max = Math.max(1, job.total); el('sme-progress').value = completed;
        el('sme-completed').textContent = `${completed} / ${job.total}`; el('sme-passed').textContent = job.passed;
        el('sme-rate').textContent = completed ? `${Math.round(job.passed / completed * 100)}%` : '—';
        el('sme-latency').textContent = results.length ? `${Math.round(results.reduce((s,r) => s + r.durationMs, 0) / results.length)} ms` : '—';
        el('sme-memory').textContent = results.length ? formatBytes(results.reduce((s,r) => s + r.hostMemoryDeltaBytes, 0)) : '—';
        const status = job.status === 'running' ? `Running case ${Math.min(completed + 1, job.total)} of ${job.total}…` : `${job.status[0].toUpperCase()}${job.status.slice(1)}${job.error ? ` · ${job.error}` : ''}`;
        el('sme-run-status').textContent = status; el('sme-export').disabled = !results.length;
        renderComparison(results); renderResults(results);
    }
    function renderComparison(results) {
        const target = el('sme-comparison'); target.replaceChildren();
        if (!results.length) { target.textContent = 'Comparison appears as cases finish.'; return; }
        const table = document.createElement('table'); const header = table.createTHead().insertRow();
        ['Model','Cases','Passed','Pass rate','Average latency','Frequent failure'].forEach(v => { const th=node('th',v); th.scope='col'; header.append(th); });
        const body = table.createTBody();
        job.models.forEach(model => {
            const rows = results.filter(r => r.model === model); if (!rows.length) return;
            const failures = new Map(); rows.filter(r => !r.passed).forEach(r => failures.set(r.errorCategory || 'unknown', (failures.get(r.errorCategory || 'unknown') || 0) + 1));
            const frequent = [...failures].sort((a,b) => b[1] - a[1])[0]; const tr = body.insertRow();
            [model, rows.length, rows.filter(r => r.passed).length, `${Math.round(rows.filter(r => r.passed).length / rows.length * 100)}%`, `${Math.round(rows.reduce((s,r) => s + r.durationMs, 0) / rows.length)} ms`, frequent ? `${frequent[0]} (${frequent[1]})` : '—'].forEach(v => tr.insertCell().textContent = v);
        }); target.append(table);
    }
    function renderResults(results) {
        const filter = el('sme-result-filter').value;
        const visible = results.filter(r => filter === 'all' || (filter === 'passed' ? r.passed : !r.passed));
        const target = el('sme-results'); target.replaceChildren();
        if (!visible.length) { target.textContent = results.length ? `No ${filter} cases.` : 'Results appear as cases finish.'; return; }
        visible.forEach(result => {
            const details = document.createElement('details'); const summary = document.createElement('summary');
            summary.append(node('span', result.passed ? 'PASS' : 'FAIL', result.passed ? 'pass' : 'fail'), document.createTextNode(` · ${result.model} · ${result.caseId} · ${result.title} · ${result.durationMs} ms`));
            details.append(summary);
            if (result.error) details.append(node('p', `${result.errorCategory}: ${result.error}`, 'fail'));
            if (result.checks?.length) {
                const table = document.createElement('table'); const h=table.createTHead().insertRow(); ['Check','Expected','Actual','Result'].forEach(v => h.append(node('th',v)));
                const body=table.createTBody(); result.checks.forEach(check => { const row=body.insertRow(); [check.kind,check.expected,check.actual || '—'].forEach(v => row.insertCell().textContent=v); const cell=row.insertCell(); cell.textContent=check.passed?'Pass':'Fail'; cell.className=check.passed?'pass':'fail'; }); details.append(table);
            }
            const json = document.createElement('details'); json.append(node('summary','Actual state JSON')); json.append(node('pre',JSON.stringify(result.actualState,null,2))); details.append(json); target.append(details);
        });
    }
    async function cancel() { if (!job) return; try { job = await api(`/api/scene-memory/evaluations/${encodeURIComponent(job.id)}/cancel`, { method:'POST' }); render(); } catch (error) { el('sme-error').textContent=error.message; } }
    function exportJson() { if (!job) return; const blob=new Blob([JSON.stringify(job,null,2)],{type:'application/json'}); const link=document.createElement('a'); link.href=URL.createObjectURL(blob); link.download=`scene-memory-evaluation-${job.id}.json`; link.click(); URL.revokeObjectURL(link.href); }

    el('sme-start').addEventListener('click', start); el('sme-cancel').addEventListener('click', cancel); el('sme-refresh-models').addEventListener('click', loadModels); el('sme-export').addEventListener('click', exportJson); el('sme-result-filter').addEventListener('change', () => job && renderResults(job.results || []));
    Promise.all([loadModels(), loadCatalog()]).catch(error => { el('sme-error').textContent = error.message; });
})();
