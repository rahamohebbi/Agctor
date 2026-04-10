/**
 * Scenario catalog editor + runtime apply actions.
 * V1: dual roster UX (runtime C# agent types + non-runtime YAML personas).
 */
(function () {
    var list = document.getElementById('scenarios-list');
    var refreshBtn = document.getElementById('scenarios-refresh');
    var empty = document.getElementById('scenarios-empty');
    var editor = document.getElementById('scenarios-editor');
    var idEl = document.getElementById('sc-id');
    var kindEl = document.getElementById('sc-kind');
    var displayEl = document.getElementById('sc-display-name');
    var descEl = document.getElementById('sc-description');
    var handlerEl = document.getElementById('sc-handler');
    var typesEl = document.getElementById('sc-agent-types');
    var chipsEl = document.getElementById('sc-agent-chips');
    var addInput = document.getElementById('sc-agent-add');
    var addBtn = document.getElementById('sc-agent-add-btn');
    var clearBtn = document.getElementById('sc-agent-clear-btn');
    var suggestionEl = document.getElementById('sc-agent-suggestions');
    var validationEl = document.getElementById('sc-agent-validation');
    var previewEl = document.getElementById('sc-agent-preview');
    var personaChipsEl = document.getElementById('sc-persona-chips');
    var personaAddInput = document.getElementById('sc-persona-add');
    var personaAddBtn = document.getElementById('sc-persona-add-btn');
    var personaClearBtn = document.getElementById('sc-persona-clear-btn');
    var personaDefaultsBtn = document.getElementById('sc-persona-defaults-btn');
    var personaSuggestionEl = document.getElementById('sc-persona-suggestions');
    var personaValidationEl = document.getElementById('sc-persona-validation');
    var personaPreviewEl = document.getElementById('sc-persona-preview');
    var bindExtractor = document.getElementById('sc-bind-extractor');
    var bindCurator = document.getElementById('sc-bind-curator');
    var bindQuery = document.getElementById('sc-bind-query');
    var saveBtn = document.getElementById('sc-save');
    var discardBtn = document.getElementById('sc-discard');
    var reloadBtn = document.getElementById('sc-reload');
    var status = document.getElementById('sc-status');
    var applyBtn = document.getElementById('sc-apply');
    var applyStatus = document.getElementById('sc-apply-status');
    var chipDefault = document.getElementById('sc-chip-default');
    var chipCurrent = document.getElementById('sc-chip-current');
    var chipDirty = document.getElementById('sc-chip-dirty');
    if (!list || !refreshBtn || !empty || !editor || !idEl || !kindEl || !displayEl || !descEl || !handlerEl || !typesEl || !chipsEl || !addInput || !addBtn || !clearBtn || !suggestionEl || !validationEl || !previewEl || !personaChipsEl || !personaAddInput || !personaAddBtn || !personaClearBtn || !personaDefaultsBtn || !personaSuggestionEl || !personaValidationEl || !personaPreviewEl || !bindExtractor || !bindCurator || !bindQuery || !saveBtn || !discardBtn || !reloadBtn || !status || !applyBtn || !applyStatus || !chipDefault || !chipCurrent || !chipDirty) return;

    var all = [];
    var selectedId = null;
    var dashboardDefaultScenario = '';
    var loadedSnapshot = '[]';
    var knownAgentTypes = [];
    var typeEnablement = {};
    var knownPersonaIds = [];

    function esc(s) { var d = document.createElement('div'); d.textContent = s == null ? '' : String(s); return d.innerHTML; }

    function api(url, opt) {
        return fetch(url, opt).then(function (r) {
            if (!r.ok) return r.json().catch(function () { return null; }).then(function (b) {
                var msg = (b && (b.message || b.errorMessage || b.error)) || ('Request failed: ' + r.status);
                throw new Error(msg);
            });
            if (r.status === 204) return null;
            return r.json();
        });
    }

    function currentScenario() {
        return all.find(function (x) { return x.id === selectedId; }) || null;
    }

    function normalizeType(t) {
        return String(t || '').trim();
    }

    function hasType(list, t) {
        var n = normalizeType(t).toLowerCase();
        return list.some(function (x) { return normalizeType(x).toLowerCase() === n; });
    }

    function hasUnsavedChanges() {
        return JSON.stringify(all) !== loadedSnapshot;
    }

    function renderHeaderChips(currentScenarioName) {
        chipDefault.textContent = dashboardDefaultScenario || '(not configured)';
        chipCurrent.textContent = currentScenarioName || '(none applied)';
        if (hasUnsavedChanges()) {
            chipDirty.textContent = 'Unsaved edits';
            chipDirty.className = 'mt-1 text-sm font-semibold text-amber-700 dark:text-amber-300';
        } else {
            chipDirty.textContent = 'No local edits';
            chipDirty.className = 'mt-1 text-sm font-semibold text-emerald-700 dark:text-emerald-300';
        }
    }

    function renderList() {
        if (!all.length) {
            list.innerHTML = '<div class="text-xs text-gray-500 dark:text-gray-400">No scenarios found.</div>';
            return;
        }

        var html = '';
        for (var i = 0; i < all.length; i++) {
            var s = all[i];
            var active = s.id === selectedId;
            html += '<button class="w-full text-left rounded border p-2 ' + (active ? 'border-blue-300 dark:border-blue-700' : 'border-gray-200 dark:border-gray-700') + ' sc-pick" data-id="' + esc(s.id) + '">';
            html += '<div class="font-medium text-gray-900 dark:text-white">' + esc(s.displayName || s.id) + '</div>';
            html += '<div class="text-xs text-gray-500 dark:text-gray-400">' + esc(s.id) + ' · ' + esc(s.kind) + '</div>';
            html += '<div class="text-[11px] mt-1 text-gray-500 dark:text-gray-400">' + ((s.agentTypes || []).length) + ' agent type(s)</div>';
            html += '</button>';
        }
        list.innerHTML = html;
        list.querySelectorAll('.sc-pick').forEach(function (btn) {
            btn.addEventListener('click', function () {
                selectedId = btn.getAttribute('data-id');
                renderList();
                renderEditor();
                applyStatus.textContent = '';
            });
        });
    }

    function renderEditor() {
        var s = currentScenario();
        if (!s) {
            empty.classList.remove('hidden');
            editor.classList.add('hidden');
            applyBtn.disabled = true;
            return;
        }
        empty.classList.add('hidden');
        editor.classList.remove('hidden');
        applyBtn.disabled = false;
        idEl.value = s.id || '';
        kindEl.value = s.kind || '';
        displayEl.value = s.displayName || '';
        descEl.value = s.description || '';
        handlerEl.value = s.handler || '';
        typesEl.value = (s.agentTypes || []).join('\n');
        bindExtractor.value = (s.personaBindings && s.personaBindings.extractor) || '';
        bindCurator.value = (s.personaBindings && s.personaBindings.curator) || '';
        bindQuery.value = (s.personaBindings && s.personaBindings.query) || '';
        renderAgentTypeEditor();
        renderPersonaEditor();
    }

    function updateScenarioFromForm() {
        var s = currentScenario();
        if (!s) return;
        s.displayName = String(displayEl.value || '').trim();
        s.description = String(descEl.value || '').trim();
        s.agentTypes = String(typesEl.value || '')
            .split('\n')
            .map(function (x) { return x.trim(); })
            .filter(function (x) { return x.length > 0; });
        s.personaAgentIds = (s.personaAgentIds || []).map(normalizeType).filter(Boolean);
        s.personaBindings = s.personaBindings || {};
        s.personaBindings.extractor = normalizeType(bindExtractor.value) || null;
        s.personaBindings.curator = normalizeType(bindCurator.value) || null;
        s.personaBindings.query = normalizeType(bindQuery.value) || null;
        renderHeaderChips(chipCurrent.textContent === '(none applied)' ? '' : chipCurrent.textContent);
        renderAgentTypeEditor();
        renderPersonaEditor();
    }

    function renderAgentTypeEditor() {
        var s = currentScenario();
        if (!s) {
            chipsEl.innerHTML = '';
            validationEl.innerHTML = '';
            previewEl.textContent = '';
            return;
        }

        var types = (s.agentTypes || []).map(normalizeType).filter(Boolean);
        chipsEl.innerHTML = '';
        if (!types.length) {
            chipsEl.innerHTML = '<span class="text-xs text-gray-400 dark:text-gray-500">No agent types selected.</span>';
        } else {
            types.forEach(function (t) {
                var known = knownAgentTypes.indexOf(t) >= 0;
                var enabled = typeEnablement[t] !== false;
                var cls = known
                    ? (enabled
                        ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-200'
                        : 'bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-200')
                    : 'bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-200';
                var title = known ? (enabled ? 'Known and enabled' : 'Known but disabled') : 'Unknown type';
                chipsEl.innerHTML +=
                    '<span class="inline-flex items-center gap-1 rounded px-2 py-1 text-xs font-medium ' + cls + '" title="' + esc(title) + '">' +
                    esc(t) +
                    '<button type="button" class="font-bold leading-none hover:opacity-80" data-remove-type="' + esc(t) + '" aria-label="Remove ' + esc(t) + '">×</button>' +
                    '</span>';
            });
        }

        var seen = {};
        var dups = [];
        types.forEach(function (t) {
            var k = t.toLowerCase();
            seen[k] = (seen[k] || 0) + 1;
            if (seen[k] === 2) dups.push(t);
        });
        var unknown = types.filter(function (t) { return knownAgentTypes.indexOf(t) < 0; });
        var disabled = types.filter(function (t) { return knownAgentTypes.indexOf(t) >= 0 && typeEnablement[t] === false; });

        var lines = [];
        if (dups.length) lines.push('<div class="text-amber-700 dark:text-amber-300">⚠ Duplicates: ' + esc(dups.join(', ')) + '</div>');
        if (unknown.length) lines.push('<div class="text-red-700 dark:text-red-300">⚠ Unknown type(s): ' + esc(unknown.join(', ')) + '</div>');
        if (disabled.length) lines.push('<div class="text-amber-700 dark:text-amber-300">⚠ Disabled in settings: ' + esc(disabled.join(', ')) + '</div>');
        if (!lines.length) lines.push('<div class="text-emerald-700 dark:text-emerald-300">✓ Roster looks valid.</div>');
        validationEl.innerHTML = lines.join('');

        var willStart = types.filter(function (t) {
            return t === 'SessionCoordinatorAgent' || t === 'SessionMemoryAgent';
        });
        var configOnly = types.filter(function (t) { return willStart.indexOf(t) < 0; });
        previewEl.innerHTML =
            'Apply impact preview: <strong>Will bootstrap:</strong> ' + esc(willStart.length ? willStart.join(', ') : '(none)') +
            ' · <strong>Configured only:</strong> ' + esc(configOnly.length ? configOnly.join(', ') : '(none)');

        chipsEl.querySelectorAll('[data-remove-type]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                removeType(btn.getAttribute('data-remove-type') || '');
            });
        });
    }

    function renderPersonaEditor() {
        var s = currentScenario();
        if (!s) {
            personaChipsEl.innerHTML = '';
            personaValidationEl.innerHTML = '';
            personaPreviewEl.textContent = '';
            return;
        }

        var personas = (s.personaAgentIds || []).map(normalizeType).filter(Boolean);
        personaChipsEl.innerHTML = '';
        if (!personas.length) {
            personaChipsEl.innerHTML = '<span class="text-xs text-gray-400 dark:text-gray-500">No personas selected.</span>';
        } else {
            personas.forEach(function (p) {
                var known = knownPersonaIds.indexOf(p) >= 0;
                var cls = known
                    ? 'bg-sky-100 text-sky-800 dark:bg-sky-900/40 dark:text-sky-200'
                    : 'bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-200';
                var title = known ? 'Known YAML persona' : 'Unknown persona id';
                personaChipsEl.innerHTML +=
                    '<span class="inline-flex items-center gap-1 rounded px-2 py-1 text-xs font-medium ' + cls + '" title="' + esc(title) + '">' +
                    esc(p) +
                    '<button type="button" class="font-bold leading-none hover:opacity-80" data-remove-persona="' + esc(p) + '" aria-label="Remove ' + esc(p) + '">×</button>' +
                    '</span>';
            });
        }

        var unknown = personas.filter(function (p) { return knownPersonaIds.indexOf(p) < 0; });
        var bind = s.personaBindings || {};
        var missingBindings = [bind.extractor, bind.curator, bind.query]
            .filter(function (x) { return !!normalizeType(x); })
            .filter(function (x) { return personas.every(function (p) { return p.toLowerCase() !== normalizeType(x).toLowerCase(); }); });

        var lines = [];
        if (unknown.length) lines.push('<div class="text-red-700 dark:text-red-300">⚠ Unknown persona id(s): ' + esc(unknown.join(', ')) + '</div>');
        if (missingBindings.length) lines.push('<div class="text-amber-700 dark:text-amber-300">⚠ Binding must reference selected persona: ' + esc(missingBindings.join(', ')) + '</div>');
        if (!lines.length) lines.push('<div class="text-emerald-700 dark:text-emerald-300">✓ Persona profile looks valid.</div>');
        personaValidationEl.innerHTML = lines.join('');
        personaPreviewEl.innerHTML =
            'Persona profile attached: <strong>' + esc(personas.length ? personas.join(', ') : '(none)') + '</strong>' +
            ' · Non-runtime only (used by project-memory pipeline, not actor spawn).';

        personaChipsEl.querySelectorAll('[data-remove-persona]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                removePersona(btn.getAttribute('data-remove-persona') || '');
            });
        });
    }

    function addType(raw) {
        var s = currentScenario();
        if (!s) return;
        var t = normalizeType(raw);
        if (!t) return;
        var list = Array.isArray(s.agentTypes) ? s.agentTypes.slice() : [];
        if (hasType(list, t)) {
            addInput.value = '';
            return;
        }
        list.push(t);
        s.agentTypes = list;
        typesEl.value = list.join('\n');
        addInput.value = '';
        renderHeaderChips(chipCurrent.textContent === '(none applied)' ? '' : chipCurrent.textContent);
        renderAgentTypeEditor();
    }

    function removeType(raw) {
        var s = currentScenario();
        if (!s) return;
        var t = normalizeType(raw).toLowerCase();
        var list = (s.agentTypes || []).filter(function (x) { return normalizeType(x).toLowerCase() !== t; });
        s.agentTypes = list;
        typesEl.value = list.join('\n');
        renderHeaderChips(chipCurrent.textContent === '(none applied)' ? '' : chipCurrent.textContent);
        renderAgentTypeEditor();
    }

    function addPersona(raw) {
        var s = currentScenario();
        if (!s) return;
        var p = normalizeType(raw);
        if (!p) return;
        var list = Array.isArray(s.personaAgentIds) ? s.personaAgentIds.slice() : [];
        if (hasType(list, p)) {
            personaAddInput.value = '';
            return;
        }
        list.push(p);
        s.personaAgentIds = list;
        personaAddInput.value = '';
        renderHeaderChips(chipCurrent.textContent === '(none applied)' ? '' : chipCurrent.textContent);
        renderPersonaEditor();
    }

    function removePersona(raw) {
        var s = currentScenario();
        if (!s) return;
        var p = normalizeType(raw).toLowerCase();
        s.personaAgentIds = (s.personaAgentIds || []).filter(function (x) { return normalizeType(x).toLowerCase() !== p; });
        renderHeaderChips(chipCurrent.textContent === '(none applied)' ? '' : chipCurrent.textContent);
        renderPersonaEditor();
    }

    function loadRuntimeBadges() {
        return Promise.all([
            api('/api/Config'),
            api('/api/Test/current-scenario'),
            api('/api/agents/definitions')
        ]).then(function (res) {
            var cfg = res[0] || {};
            var cur = res[1];
            var defs = Array.isArray(res[2]) ? res[2] : [];
            dashboardDefaultScenario = cfg.dashboardScenarioName || '';
            knownAgentTypes = Object.keys(cfg.agentTypes || {}).sort();
            typeEnablement = cfg.agentTypeEnablement || {};
            suggestionEl.innerHTML = knownAgentTypes.map(function (t) { return '<option value="' + esc(t) + '"></option>'; }).join('');
            knownPersonaIds = defs
                .filter(function (d) { return d && d.kind === 'project-memory-yaml' && d.id; })
                .map(function (d) { return d.id; })
                .sort();
            personaSuggestionEl.innerHTML = knownPersonaIds.map(function (p) { return '<option value="' + esc(p) + '"></option>'; }).join('');
            renderHeaderChips(cur && cur.scenarioName ? cur.scenarioName : '');
        });
    }

    function loadCatalog() {
        status.textContent = 'Loading catalog...';
        return api('/api/scenarios')
            .then(function (items) {
                all = Array.isArray(items) ? items : [];
                loadedSnapshot = JSON.stringify(all);
                if (!selectedId && all.length) selectedId = all[0].id;
                if (selectedId && !all.find(function (x) { return x.id === selectedId; })) selectedId = all.length ? all[0].id : null;
                renderList();
                renderEditor();
                status.textContent = '';
                renderHeaderChips(chipCurrent.textContent === '(none applied)' ? '' : chipCurrent.textContent);
            })
            .catch(function (e) { status.textContent = e.message || 'Load failed'; });
    }

    function loadAll() {
        return loadRuntimeBadges().then(loadCatalog).catch(function (e) {
            status.textContent = e.message || 'Load failed';
        });
    }

    saveBtn.addEventListener('click', function () {
        updateScenarioFromForm();
        saveBtn.disabled = true;
        status.textContent = 'Saving catalog...';
        api('/api/scenarios', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ version: 1, scenarios: all })
        })
            .then(function () {
                status.textContent = 'Catalog saved';
                return loadCatalog();
            })
            .catch(function (e) { status.textContent = e.message || 'Save failed'; })
            .finally(function () { saveBtn.disabled = false; });
    });

    discardBtn.addEventListener('click', function () {
        var s = currentScenario();
        if (!s) return;
        var saved = JSON.parse(loadedSnapshot);
        var fromSaved = saved.find(function (x) { return x.id === selectedId; });
        if (!fromSaved) return;
        s.displayName = fromSaved.displayName || '';
        s.description = fromSaved.description || '';
        s.agentTypes = (fromSaved.agentTypes || []).slice();
        s.personaAgentIds = (fromSaved.personaAgentIds || []).slice();
        s.personaBindings = {
            extractor: fromSaved.personaBindings && fromSaved.personaBindings.extractor || null,
            curator: fromSaved.personaBindings && fromSaved.personaBindings.curator || null,
            query: fromSaved.personaBindings && fromSaved.personaBindings.query || null
        };
        renderEditor();
        renderHeaderChips(chipCurrent.textContent === '(none applied)' ? '' : chipCurrent.textContent);
        status.textContent = 'Changes discarded';
    });

    reloadBtn.addEventListener('click', function () {
        reloadBtn.disabled = true;
        status.textContent = 'Reloading from disk...';
        api('/api/scenarios/reload', { method: 'POST' })
            .then(function () { return loadCatalog(); })
            .finally(function () { reloadBtn.disabled = false; });
    });

    applyBtn.addEventListener('click', function () {
        if (!selectedId) return;
        applyBtn.disabled = true;
        applyStatus.textContent = 'Applying...';
        api('/api/scenarios/' + encodeURIComponent(selectedId) + '/apply', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ parameters: {} })
        })
            .then(function (resp) {
                var count = resp && resp.createdAgentIds ? resp.createdAgentIds.length : 0;
                applyStatus.textContent = 'Applied. ' + count + ' agent(s) reported created.';
                return loadRuntimeBadges();
            })
            .catch(function (e) {
                applyStatus.textContent = e.message || 'Apply failed';
            })
            .finally(function () {
                applyBtn.disabled = false;
            });
    });

    displayEl.addEventListener('input', updateScenarioFromForm);
    descEl.addEventListener('input', updateScenarioFromForm);
    typesEl.addEventListener('input', updateScenarioFromForm);
    addBtn.addEventListener('click', function () { addType(addInput.value); });
    clearBtn.addEventListener('click', function () {
        var s = currentScenario();
        if (!s) return;
        s.agentTypes = [];
        typesEl.value = '';
        renderHeaderChips(chipCurrent.textContent === '(none applied)' ? '' : chipCurrent.textContent);
        renderAgentTypeEditor();
    });
    addInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            addType(addInput.value);
        }
    });
    personaAddBtn.addEventListener('click', function () { addPersona(personaAddInput.value); });
    personaClearBtn.addEventListener('click', function () {
        var s = currentScenario();
        if (!s) return;
        s.personaAgentIds = [];
        renderHeaderChips(chipCurrent.textContent === '(none applied)' ? '' : chipCurrent.textContent);
        renderPersonaEditor();
    });
    personaDefaultsBtn.addEventListener('click', function () {
        var s = currentScenario();
        if (!s) return;
        ['person-extractor', 'memory-curator', 'person-query'].forEach(function (p) {
            if (knownPersonaIds.indexOf(p) >= 0 && !hasType(s.personaAgentIds || [], p)) {
                s.personaAgentIds = (s.personaAgentIds || []).concat([p]);
            }
        });
        if (!normalizeType(bindExtractor.value) && hasType(s.personaAgentIds || [], 'person-extractor')) bindExtractor.value = 'person-extractor';
        if (!normalizeType(bindCurator.value) && hasType(s.personaAgentIds || [], 'memory-curator')) bindCurator.value = 'memory-curator';
        if (!normalizeType(bindQuery.value) && hasType(s.personaAgentIds || [], 'person-query')) bindQuery.value = 'person-query';
        updateScenarioFromForm();
    });
    personaAddInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter') {
            e.preventDefault();
            addPersona(personaAddInput.value);
        }
    });
    bindExtractor.addEventListener('input', updateScenarioFromForm);
    bindCurator.addEventListener('input', updateScenarioFromForm);
    bindQuery.addEventListener('input', updateScenarioFromForm);
    refreshBtn.addEventListener('click', loadAll);

    loadAll();
})();

