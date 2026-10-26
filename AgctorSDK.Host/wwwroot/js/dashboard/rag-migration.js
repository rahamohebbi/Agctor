(function (global) {
    'use strict';
    var dialog = document.getElementById('rag-migration');
    if (!dialog) return;
    var el = function (id) { return document.getElementById('rm-' + id); };
    // Static assets can update before the host's compiled Razor views are restarted.
    // Supply additive controls for older markup instead of aborting initialization.
    if (!el('readiness')) {
        var readinessPanel = document.createElement('div'); readinessPanel.id = 'rm-readiness';
        readinessPanel.className = 'rm-readiness'; readinessPanel.hidden = true;
        var healthText = document.createElement('p'); healthText.id = 'rm-health';
        healthText.setAttribute('role', 'status'); healthText.setAttribute('aria-live', 'polite');
        var healthActions = document.createElement('div');
        var recheck = document.createElement('button'); recheck.id = 'rm-recheck'; recheck.type = 'button'; recheck.textContent = 'Check connection';
        var configure = document.createElement('a'); configure.href = '/Dashboard/RagProviders';
        configure.target = '_blank'; configure.rel = 'noopener'; configure.textContent = 'Start or configure provider ↗';
        healthActions.append(recheck, document.createTextNode(' '), configure);
        readinessPanel.append(healthText, healthActions); el('review').before(readinessPanel);
    }
    var scenario, job, selected = new Set(), timer, generation = 0, busy = false, readiness = null, healthRevision = 0;
    function running() { return job && ['queued', 'running'].includes(job.status); }
    async function api(path, body) {
        var response = await fetch('/api/rag-migrations' + path, body === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        if (response.status === 404 && path.endsWith('/readiness'))
            throw new Error('This running AGCTOR host needs a restart to enable connection checks. Your previews and transfer history are still available.');
        var data;
        try { data = await response.json(); }
        catch (_) { throw new Error('Migration request failed (HTTP ' + response.status + '). Reload the page or restart AGCTOR.'); }
        if (!response.ok) throw new Error(data.error || 'Could not load migration. Please try again.');
        return data;
    }
    function destination() {
        el('destination').textContent = el('provider').value === 'LightRAG'
            ? 'LightRAG uses its shared index. Other scenarios using this instance can retrieve these files.'
            : 'Destination collection: ' + scenario + '. Flow steps with a custom collection use that collection instead.';
    }
    async function checkConnection() {
        var token = generation, request = ++healthRevision, provider = job ? job.providerId : el('provider').value;
        readiness = null; el('readiness').hidden = !provider;
        if (!provider) { buttons(); return; }
        el('readiness').className = 'rm-readiness';
        el('health').textContent = 'Checking ' + provider + ' connection…'; el('recheck').disabled = true; buttons();
        try {
            var result = await api('/providers/' + encodeURIComponent(provider) + '/readiness');
            if (token !== generation || request !== healthRevision) return;
            readiness = result;
            el('readiness').className = 'rm-readiness ' + (result.ready ? 'ready' : 'offline');
            el('health').textContent = (result.ready ? provider + ' is reachable. ' : provider + ' is not ready. ') + result.message;
        } catch (e) { if (token === generation && request === healthRevision) el('health').textContent = e.message; }
        finally { if (token === generation && request === healthRevision) { el('recheck').disabled = false; buttons(); } }
    }
    function buttons() {
        el('action').hidden = !!job && job.status !== 'preview';
        el('action').disabled = busy || (!!job && (selected.size === 0 || !readiness || !readiness.ready || readiness.providerId !== job.providerId)) || !el('provider').value;
        el('action').textContent = busy ? 'Working…' : !job ? 'Preview files' : 'Copy ' + selected.size + ' file' + (selected.size === 1 ? '' : 's') + ' to ' + job.providerId;
        el('back').hidden = !job || running(); el('back').disabled = busy;
        el('stop').hidden = !running(); el('stop').disabled = busy || !!(job && job.stopRequested);
        el('provider').disabled = el('source').disabled = el('force').disabled = busy || !!job;
        el('all').disabled = busy;
        el('files').querySelectorAll('input').forEach(function (input) { input.disabled = busy; });
    }
    function files() {
        el('files').replaceChildren();
        var filter = el('filter').value.toLowerCase();
        job.files.filter(function (f) { return f.path.toLowerCase().includes(filter); }).forEach(function (file) {
            var row = document.createElement('div'); row.className = 'rm-file';
            if (job.status === 'preview' && ['pending', 'review'].includes(file.status)) {
                var check = document.createElement('input'); check.type = 'checkbox'; check.checked = selected.has(file.path);
                check.setAttribute('aria-label', 'Include ' + file.path); check.disabled = busy;
                check.addEventListener('change', function () { if (check.checked) selected.add(file.path); else selected.delete(file.path); counts(); buttons(); }); row.append(check);
            }
            var text = document.createElement('div'); text.className = 'rm-file-text';
            var path = document.createElement('div'); path.className = 'rm-file-path'; path.textContent = file.path;
            var note = document.createElement('small'); note.textContent = (file.bytes ? (file.bytes / 1024).toFixed(1) + ' KB · ' : '') + file.message;
            var badge = document.createElement('span'); badge.className = 'rm-badge ' + file.status; badge.textContent = file.status === 'pending' ? 'Ready' : file.status;
            text.append(path, note); row.append(text, badge); el('files').append(row);
        });
        if (!job.files.length) el('files').textContent = job.source === 'legacy' ? 'The legacy people/ folder has no markdown. Choose New preview, then This scenario’s markdown to find scenario memory.' : 'No markdown files found for this scenario. Try the legacy people/ folder if your memory predates scenarios.';
    }
    function counts() {
        var count = function (status) { return job.files.filter(function (f) { return f.status === status; }).length; };
        el('counts').textContent = job.status === 'preview' ? selected.size + ' selected · ' + count('unchanged') + ' already accepted · ' + count('excluded') + ' excluded · ' + count('review') + ' need review'
            : count('accepted') + ' accepted · ' + count('unchanged') + ' unchanged · ' + (count('failed') + count('uncertain')) + ' need attention · ' + (count('pending') + count('sending')) + ' remaining';
        var available = count('pending'), readySelected = job.files.filter(function (f) { return f.status === 'pending' && selected.has(f.path); }).length;
        el('all').checked = available > 0 && readySelected === available;
        el('all').indeterminate = readySelected > 0 && readySelected < available;
        el('progress').max = Math.max(1, job.files.length);
        el('progress').value = job.files.length - count('pending') - count('sending');
    }
    function render() {
        el('setup').hidden = !!job;
        el('review').hidden = !job;
        dialog.querySelectorAll('[data-rm-step]').forEach(function (step) { step.classList.toggle('active', Number(step.dataset.rmStep) === (!job ? 1 : job.status === 'preview' ? 2 : 3)); });
        if (job) { el('provider').value = job.providerId; destination(); }
        el('scenario').textContent = scenario + (job ? ' · ' + job.providerId + ' · ' + (job.providerId === 'LightRAG' ? 'shared index' : job.collectionId) : ' · Markdown → external RAG');
        if (job) {
            el('select-label').hidden = job.status !== 'preview';
            el('progress').hidden = job.status === 'preview';
            el('status').textContent = job.message;
            counts(); files();
        }
        buttons();
    }
    async function history(token, resume) {
        try {
            var jobs = await api('?scenarioId=' + encodeURIComponent(scenario));
            if (token !== generation) return;
            el('history-list').replaceChildren();
            jobs.forEach(function (item) {
                var button = document.createElement('button'); button.type = 'button';
                button.textContent = item.providerId + ' · ' + item.status + ' · ' + new Date(item.createdAt).toLocaleString() + ' · ' + (item.source === 'legacy' ? 'Legacy folder' : 'Scenario markdown') + ' · ' + item.files.length + ' files';
                if (item.status === 'failed' || item.status === 'interrupted') { var reason = document.createElement('small'); reason.textContent = item.message; button.append(reason); }
                button.addEventListener('click', function () { if (busy) return; job = item; selected.clear(); render(); checkConnection(); poll(); });
                el('history-list').append(button);
            });
            if (!jobs.length) el('history-list').textContent = 'No transfers yet.';
            var active = jobs.find(function (j) { return ['running', 'queued'].includes(j.status); });
            if (resume && !job && active) { job = active; render(); checkConnection(); poll(); }
        } catch (e) { if (token === generation) el('history-list').textContent = e.message; }
    }
    function poll() {
        clearTimeout(timer);
        if (!running() || !dialog.open) return;
        var token = generation, id = job.id;
        timer = setTimeout(async function () {
            try {
                var update = await api('/' + encodeURIComponent(id));
                if (token !== generation || !dialog.open || !job || job.id !== id) return;
                job = update; render(); if (!running()) history(token); poll();
            } catch (e) { if (token === generation && dialog.open) { el('status').textContent = e.message + ' Reconnecting…'; poll(); } }
        }, 1500);
    }
    el('action').addEventListener('click', async function () {
        var token = generation;
        busy = true; buttons();
        try {
            var result = !job ? await api('/preview', { scenarioId: scenario, providerId: el('provider').value, source: el('source').value, force: el('force').checked })
                : await api('/' + job.id + '/start', { paths: Array.from(selected) });
            if (token !== generation) return;
            job = result; selected = new Set(job.files.filter(function (f) { return f.status === 'pending'; }).map(function (f) { return f.path; }));
            render(); poll(); history(token);
        } catch (e) { if (token === generation) el('status').textContent = e.message; }
        finally { if (token === generation) { busy = false; buttons(); if (job) files(); } }
    });
    el('back').addEventListener('click', function () {
        if (job) { el('provider').value = job.providerId; el('source').value = job.source; }
        job = null; selected.clear(); el('force').checked = false; el('status').textContent = 'Preview again to skip accepted files and review anything remaining. Uncertain submissions may already exist in the provider.';
        destination(); render(); checkConnection();
    });
    el('stop').addEventListener('click', async function () {
        var token = generation; busy = true; buttons();
        try { var update = await api('/' + job.id + '/stop', {}); if (token === generation) { job = update; render(); } }
        catch (e) { if (token === generation) el('status').textContent = e.message; }
        finally { if (token === generation) { busy = false; buttons(); } }
    });
    el('all').addEventListener('change', function () { selected = new Set(el('all').checked ? job.files.filter(function (f) { return f.status === 'pending'; }).map(function (f) { return f.path; }) : []); render(); });
    el('filter').addEventListener('input', function () { if (job) files(); });
    el('provider').addEventListener('change', function () { destination(); checkConnection(); });
    el('recheck').addEventListener('click', checkConnection);
    dialog.querySelectorAll('[data-rm-close]').forEach(function (button) { button.addEventListener('click', function () { dialog.close(); }); });
    dialog.addEventListener('close', function () { clearTimeout(timer); generation++; busy = false; });
    global.AgctorRagMigration = { open: async function (scenarioId, providerId) {
        if (!scenarioId) return;
        var token = ++generation; scenario = scenarioId; job = null; busy = true; selected.clear(); readiness = null; healthRevision++; el('readiness').hidden = true;
        el('filter').value = ''; el('force').checked = false; el('source').value = 'scenario';
        el('status').textContent = 'Loading destination providers…'; el('history-list').replaceChildren();
        render(); dialog.showModal();
        try {
            var data = await global.AgctorRag.load(true);
            if (token !== generation) return;
            el('provider').replaceChildren(new Option('Choose a destination', ''));
            data.providers.filter(function (id) { return id !== 'None'; }).forEach(function (id) { el('provider').add(new Option(id, id)); });
            el('provider').value = providerId && providerId !== 'None' ? providerId : data.globalDefault !== 'None' ? data.globalDefault : '';
            destination(); el('status').textContent = 'Preview first. Nothing is sent until you choose Copy files.';
            checkConnection(); history(token, true);
        } catch (e) { if (token === generation) el('status').textContent = e.message; }
        finally { if (token === generation) { busy = false; buttons(); } }
    } };
})(window);
