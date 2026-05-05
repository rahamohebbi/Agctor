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
    /** Default-catalog ids hidden via user file (server); included on catalog save. */
    var suppressedDefaults = [];
    var selectedId = null;
    var dashboardDefaultScenario = '';
    var loadedSnapshot = '[]';
    var loadedSuppressedSnapshot = '[]';
    var knownAgentTypes = [];
    var typeEnablement = {};
    var knownPersonaIds = [];

    function esc(s) { var d = document.createElement('div'); d.textContent = s == null ? '' : String(s); return d.innerHTML; }

    function api(url, opt) {
        return fetch(url, opt).then(function (r) {
            if (!r.ok) return r.json().catch(function () { return null; }).then(function (b) {
                var msg = (b && (b.message || b.errorMessage || b.error)) || ('Request failed: ' + r.status);
                if (b && Array.isArray(b.details) && b.details.length)
                    msg += ' ' + b.details.join('; ');
                throw new Error(msg);
            });
            if (r.status === 204) return null;
            return r.json();
        });
    }

    function currentScenario() {
        return all.find(function (x) { return x.id === selectedId; }) || null;
    }

    /** Prefer camelCase from API; tolerate PascalCase if an older server returned it. */
    function getScenarioFlow(s) {
        if (!s) return null;
        if (s.flow != null) return s.flow;
        if (s.Flow != null) return s.Flow;
        return null;
    }

    function normalizeType(t) {
        return String(t || '').trim();
    }

    function hasType(list, t) {
        var n = normalizeType(t).toLowerCase();
        return list.some(function (x) { return normalizeType(x).toLowerCase() === n; });
    }

    function hasUnsavedChanges() {
        return (
            JSON.stringify(all) !== loadedSnapshot ||
            JSON.stringify(suppressedDefaults || []) !== loadedSuppressedSnapshot
        );
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
            html += '<div class="text-[11px] mt-1 text-gray-500 dark:text-gray-400">' + ((s.agentTypes || []).length) + ' agent type(s)' + (getScenarioFlow(s) ? ' · flow' : '') + '</div>';
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
        return Promise.all([api('/api/scenarios'), api('/api/scenarios/suppressed-default-ids')])
            .then(function (pair) {
                var items = pair[0];
                var sup = pair[1];
                all = Array.isArray(items) ? items : [];
                suppressedDefaults = Array.isArray(sup) ? sup.slice() : [];
                loadedSnapshot = JSON.stringify(all);
                loadedSuppressedSnapshot = JSON.stringify(suppressedDefaults);
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
            body: JSON.stringify({
                version: 1,
                scenarios: all,
                suppressedDefaultScenarioIds: suppressedDefaults
            })
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
        var savedFlow = getScenarioFlow(fromSaved);
        if (savedFlow) s.flow = JSON.parse(JSON.stringify(savedFlow));
        else delete s.flow;
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

    var newScenarioBtn = document.getElementById('sc-new-scenario');
    if (newScenarioBtn) {
        newScenarioBtn.addEventListener('click', function () {
            var rawId = window.prompt('New scenario id (letters, digits, - _ . only):', '');
            if (rawId == null) return;
            var id = String(rawId || '').trim();
            if (!id) {
                status.textContent = 'Id required.';
                return;
            }
            var dn = window.prompt('Display name (optional, defaults to id):', id);
            if (dn == null) return;
            var disp = String(dn || '').trim() || id;
            status.textContent = 'Creating…';
            newScenarioBtn.disabled = true;
            api('/api/scenarios', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ id: id, displayName: disp, description: '' })
            })
                .then(function (created) {
                    status.textContent = 'Created.';
                    selectedId = (created && created.id) || id;
                    return loadCatalog();
                })
                .catch(function (e) {
                    status.textContent = e.message || 'Create failed';
                })
                .finally(function () {
                    newScenarioBtn.disabled = false;
                });
        });
    }

    var delScenarioBtn = document.getElementById('sc-delete-scenario');
    if (delScenarioBtn) {
        delScenarioBtn.addEventListener('click', function () {
            if (!selectedId) return;
            if (!window.confirm('Delete scenario "' + selectedId + '" from the user catalog? Built-in defaults require a second delete to hide.')) return;
            status.textContent = 'Deleting…';
            delScenarioBtn.disabled = true;
            api('/api/scenarios/' + encodeURIComponent(selectedId), { method: 'DELETE' })
                .then(function () {
                    status.textContent = 'Deleted.';
                    selectedId = null;
                    return loadCatalog();
                })
                .catch(function (e) {
                    status.textContent = e.message || 'Delete failed';
                })
                .finally(function () {
                    delScenarioBtn.disabled = false;
                });
        });
    }

    /** PRD-014: modal flow designer (Cytoscape behind adapter). */
    (function wireFlowModal() {
        var openBtn = document.getElementById('sc-open-flow');
        var modal = document.getElementById('sc-flow-modal');
        var cyHost = document.getElementById('sc-flow-cy');
        var msgEl = document.getElementById('sc-flow-message');
        var btnValidate = document.getElementById('sc-flow-validate');
        var btnSimulate = document.getElementById('sc-flow-simulate');
        var btnSaveFlow = document.getElementById('sc-flow-save');
        var btnConnect = document.getElementById('sc-flow-connect');
        var btnDeleteEdges = document.getElementById('sc-flow-delete-edges');
        var routerPanel = document.getElementById('sc-flow-router-panel');
        var routerModeEl = document.getElementById('sc-flow-router-mode');
        var routerMaxEl = document.getElementById('sc-flow-router-max');
        var routerMinConfEl = document.getElementById('sc-flow-router-minconf');
        var routerFallbackEl = document.getElementById('sc-flow-router-fallback');
        var routerCandUl = document.getElementById('sc-flow-router-candidates');
        var personaPanel = document.getElementById('sc-flow-persona-panel');
        var personaSelect = document.getElementById('sc-flow-persona-select');
        var personaRosterHint = document.getElementById('sc-flow-persona-roster-hint');
        var personaInvalidHint = document.getElementById('sc-flow-persona-invalid-hint');
        var personaCapEl = document.getElementById('sc-flow-persona-cap');
        if (!openBtn || !modal || !cyHost || !msgEl || !btnValidate || !btnSimulate || !btnSaveFlow || !btnConnect) return;
        if (!window.AgctorScenarioFlow || typeof window.AgctorScenarioFlow.createGraphRenderer !== 'function') return;

        var renderer = null;
        var draftBase = null;
        /** PRD-014 Phase 11: `id.toLowerCase()` → agent row from `GET /api/project-memory/agents`. */
        var flowModalAgentLabels = {};

        function setFlowMsg(html) {
            msgEl.innerHTML = html || '';
        }

        function closeModal() {
            if (renderer) {
                renderer.destroy();
                renderer = null;
            }
            draftBase = null;
            if (routerPanel) routerPanel.classList.add('hidden');
            if (personaPanel) personaPanel.classList.add('hidden');
            if (personaCapEl) {
                personaCapEl.innerHTML = '';
                personaCapEl.classList.add('hidden');
            }
            modal.classList.add('hidden');
            cyHost.innerHTML = '';
        }

        function loadFlowModalAgentLabels() {
            return api('/api/project-memory/agents').then(function (rows) {
                flowModalAgentLabels = {};
                (rows || []).forEach(function (a) {
                    if (a && a.id) flowModalAgentLabels[normalizeType(a.id).toLowerCase()] = a;
                });
            }).catch(function () {
                flowModalAgentLabels = {};
            });
        }

        function getFlowPersonaLabel(personaId) {
            var pid = normalizeType(personaId);
            if (!pid) return '';
            var row = flowModalAgentLabels[pid.toLowerCase()];
            if (!row) return pid;
            var n = String(row.name || '').trim();
            if (n) return n;
            var r = String(row.role || '').trim();
            if (r) return r + ' — ' + pid;
            return pid;
        }

        /** Renders YAML-derived I/O, tools, memory paths, guardrails from bulk <code>GET /api/project-memory/agents</code>. */
        function strListHtml(items) {
            if (!items || !items.length) return '';
            var h = '<ul class="mt-0.5 list-inside list-disc space-y-0.5 pl-0.5 font-mono text-[9px]">';
            for (var i = 0; i < items.length; i++) {
                h += '<li>' + esc(items[i]) + '</li>';
            }
            return h + '</ul>';
        }

        function renderFlowPersonaCapabilityPanel(personaId) {
            if (!personaCapEl) return;
            var pid = normalizeType(personaId);
            if (!pid) {
                personaCapEl.classList.add('hidden');
                personaCapEl.innerHTML = '';
                return;
            }
            var row = flowModalAgentLabels[pid.toLowerCase()];
            if (!row) {
                personaCapEl.classList.remove('hidden');
                personaCapEl.innerHTML =
                    '<p class="text-amber-800 dark:text-amber-200">' +
                    esc('No metadata for "' + pid + '". Set project root, refresh scenarios, then reopen this modal.') +
                    '</p>';
                return;
            }
            var chunks = [];
            if (row.description) {
                chunks.push('<p class="mb-1.5 leading-snug text-gray-700 dark:text-gray-300">' + esc(row.description) + '</p>');
            }
            if (row.projectTypes && row.projectTypes.length) {
                chunks.push(
                    '<p class="mb-1"><span class="font-semibold text-gray-900 dark:text-gray-100">Project types:</span> ' +
                    esc(row.projectTypes.join(', ')) +
                    '</p>'
                );
            }
            chunks.push(
                '<p class="mb-1"><span class="font-semibold text-gray-900 dark:text-gray-100">I/O:</span> ' +
                '<span class="font-mono text-[9px]">' +
                esc(row.inputType || '—') +
                '</span> → <span class="font-mono text-[9px]">' +
                esc(row.outputType || '—') +
                '</span></p>'
            );
            if (row.toolsAllow && row.toolsAllow.length) {
                chunks.push('<p class="mt-1 font-semibold text-gray-900 dark:text-gray-100">Tools allow</p>' + strListHtml(row.toolsAllow));
            }
            if (row.toolsDeny && row.toolsDeny.length) {
                chunks.push('<p class="mt-1 font-semibold text-gray-900 dark:text-gray-100">Tools deny</p>' + strListHtml(row.toolsDeny));
            }
            if (row.memoryRead && row.memoryRead.length) {
                chunks.push('<p class="mt-1 font-semibold text-gray-900 dark:text-gray-100">Memory read</p>' + strListHtml(row.memoryRead));
            }
            if (row.memoryWrite && row.memoryWrite.length) {
                chunks.push('<p class="mt-1 font-semibold text-gray-900 dark:text-gray-100">Memory write</p>' + strListHtml(row.memoryWrite));
            }
            if (row.guardrails && row.guardrails.length) {
                chunks.push('<p class="mt-1 font-semibold text-gray-900 dark:text-gray-100">Guardrails</p>' + strListHtml(row.guardrails));
            }
            personaCapEl.innerHTML = chunks.join('');
            personaCapEl.classList.remove('hidden');
        }

        function refreshFlowInspectors() {
            refreshFlowRouterInspector();
            refreshFlowPersonaInspector();
        }

        function flowDocHasLlmRouter(doc) {
            var nodes = (doc && doc.nodes) || [];
            return nodes.some(function (n) {
                if (!n || n.type !== 'Router' || !n.config) return false;
                return String(n.config.routerMode || '').toLowerCase() === 'llm';
            });
        }

        function refreshFlowRouterInspector() {
            if (!renderer || !routerPanel || !routerModeEl || !routerMaxEl || !routerMinConfEl || !routerFallbackEl || !routerCandUl) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) {
                routerPanel.classList.add('hidden');
                return;
            }
            var sel = cy.$('node:selected');
            if (sel.length !== 1 || sel.data('agctorType') !== 'Router') {
                routerPanel.classList.add('hidden');
                return;
            }
            routerPanel.classList.remove('hidden');
            var cfg = {};
            try {
                cfg = JSON.parse(sel.data('agctorConfig') || '{}');
            } catch (e) {
                cfg = {};
            }
            routerModeEl.value = (cfg.routerMode === 'llm') ? 'llm' : 'deterministic';
            routerMaxEl.value = cfg.maxTargets != null && cfg.maxTargets !== '' ? String(cfg.maxTargets) : '';
            routerMinConfEl.value = cfg.minConfidence != null && cfg.minConfidence !== '' ? String(cfg.minConfidence) : '';
            routerFallbackEl.value = cfg.fallbackPersonaId ? String(cfg.fallbackPersonaId) : '';
            var doc = renderer.read(JSON.parse(JSON.stringify(draftBase)));
            var rid = sel.id();
            routerCandUl.innerHTML = '';
            var edges = (doc.edges || []).filter(function (e) {
                return e && e.fromNodeId === rid && (!e.mode || e.mode === 'sequential');
            });
            edges.sort(function (a, b) { return String(a.id || '').localeCompare(String(b.id || '')); });
            edges.forEach(function (e) {
                var tn = (doc.nodes || []).filter(function (n) { return n && n.id === e.toNodeId; })[0];
                if (!tn || tn.type !== 'LlmNode') return;
                var pid = (tn.config && tn.config.personaId) ? tn.config.personaId : '(no personaId)';
                var li = document.createElement('li');
                li.textContent = tn.id + ' → ' + (pid === '(no personaId)' ? pid : (getFlowPersonaLabel(pid) + ' (' + pid + ')'));
                routerCandUl.appendChild(li);
            });
        }

        function refreshFlowPersonaInspector() {
            if (!renderer || !personaPanel || !personaSelect || !personaRosterHint || !personaInvalidHint) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) {
                personaPanel.classList.add('hidden');
                if (personaCapEl) {
                    personaCapEl.innerHTML = '';
                    personaCapEl.classList.add('hidden');
                }
                return;
            }
            var sel = cy.$('node:selected');
            if (sel.length !== 1 || sel.data('agctorType') !== 'LlmNode') {
                personaPanel.classList.add('hidden');
                if (personaCapEl) {
                    personaCapEl.innerHTML = '';
                    personaCapEl.classList.add('hidden');
                }
                return;
            }
            personaPanel.classList.remove('hidden');
            var s = currentScenario();
            var roster = (s && s.personaAgentIds) ? s.personaAgentIds.map(normalizeType).filter(Boolean) : [];
            personaRosterHint.classList.toggle('hidden', roster.length > 0);
            var cfg = {};
            try {
                cfg = JSON.parse(sel.data('agctorConfig') || '{}');
            } catch (e) {
                cfg = {};
            }
            var cur = normalizeType(cfg.personaId);
            personaInvalidHint.classList.add('hidden');
            personaInvalidHint.textContent = '';
            if (cur && roster.length > 0 && !hasType(roster, cur)) {
                personaInvalidHint.textContent = 'personaId "' + cur + '" is not on this scenario roster — pick a listed persona or add it on the scenario form.';
                personaInvalidHint.classList.remove('hidden');
            }
            var idsForSelect = roster.slice();
            if (cur && !hasType(idsForSelect, cur)) idsForSelect.push(cur);
            idsForSelect.sort(function (a, b) {
                return getFlowPersonaLabel(a).localeCompare(getFlowPersonaLabel(b), undefined, { sensitivity: 'base' });
            });
            personaSelect.innerHTML = '';
            if (!idsForSelect.length) {
                var o0 = document.createElement('option');
                o0.value = '';
                o0.textContent = '(add YAML personas on scenario form)';
                personaSelect.appendChild(o0);
            } else {
                idsForSelect.forEach(function (id) {
                    var o = document.createElement('option');
                    o.value = id;
                    o.textContent = getFlowPersonaLabel(id) + ' (' + id + ')';
                    personaSelect.appendChild(o);
                });
            }
            if (cur) {
                var matchOpt = Array.prototype.slice.call(personaSelect.options).some(function (opt) {
                    return normalizeType(opt.value).toLowerCase() === cur.toLowerCase();
                });
                if (matchOpt) {
                    for (var i = 0; i < personaSelect.options.length; i++) {
                        if (normalizeType(personaSelect.options[i].value).toLowerCase() === cur.toLowerCase()) {
                            personaSelect.selectedIndex = i;
                            break;
                        }
                    }
                } else if (personaSelect.options.length) {
                    personaSelect.selectedIndex = 0;
                }
            } else if (personaSelect.options.length) {
                personaSelect.selectedIndex = 0;
            }
            var effectivePid = normalizeType(personaSelect.value || cur);
            renderFlowPersonaCapabilityPanel(effectivePid);
        }

        function persistFlowPersonaInspector() {
            if (!renderer || !personaSelect) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) return;
            var n = cy.$('node:selected');
            if (n.length !== 1 || n.data('agctorType') !== 'LlmNode') return;
            var cfg = {};
            try {
                cfg = JSON.parse(n.data('agctorConfig') || '{}');
            } catch (e) {
                cfg = {};
            }
            cfg.personaId = normalizeType(personaSelect.value);
            n.data('agctorConfig', JSON.stringify(cfg));
            setFlowMsg('');
            refreshFlowPersonaInspector();
        }

        function persistFlowRouterInspector() {
            if (!renderer || !routerModeEl) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) return;
            var n = cy.$('node:selected');
            if (n.length !== 1 || n.data('agctorType') !== 'Router') return;
            var cfg = {};
            try {
                cfg = JSON.parse(n.data('agctorConfig') || '{}');
            } catch (e) {
                cfg = {};
            }
            if (routerModeEl.value === 'llm') {
                cfg.routerMode = 'llm';
                var mx = routerMaxEl.value.trim();
                if (mx) {
                    var nmx = parseInt(mx, 10);
                    if (!isNaN(nmx) && nmx > 0) cfg.maxTargets = nmx;
                    else delete cfg.maxTargets;
                } else delete cfg.maxTargets;
                var mc = routerMinConfEl.value.trim();
                if (mc) {
                    var nmc = parseFloat(mc);
                    if (!isNaN(nmc)) cfg.minConfidence = nmc;
                    else delete cfg.minConfidence;
                } else delete cfg.minConfidence;
                var fb = routerFallbackEl.value.trim();
                if (fb) cfg.fallbackPersonaId = fb;
                else delete cfg.fallbackPersonaId;
            } else {
                delete cfg.routerMode;
                delete cfg.maxTargets;
                delete cfg.minConfidence;
                delete cfg.fallbackPersonaId;
            }
            n.data('agctorConfig', JSON.stringify(cfg));
            setFlowMsg('');
        }

        function openModal() {
            var s = currentScenario();
            if (!s) return;
            var existingFlow = getScenarioFlow(s);
            draftBase = existingFlow ? JSON.parse(JSON.stringify(existingFlow)) : window.AgctorScenarioFlow.emptyFlow(s.id);
            renderer = window.AgctorScenarioFlow.createGraphRenderer();
            cyHost.innerHTML = '';
            modal.classList.remove('hidden');
            requestAnimationFrame(function () {
                requestAnimationFrame(function () {
                    renderer.mount(cyHost, draftBase);
                    renderer.onChange(function () {
                        setFlowMsg('');
                        refreshFlowInspectors();
                    });
                    var cy0 = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
                    if (cy0) {
                        cy0.on('select unselect', refreshFlowInspectors);
                    }
                    refreshFlowInspectors();
                    loadFlowModalAgentLabels().finally(function () {
                        refreshFlowInspectors();
                    });
                    if (typeof renderer.fitViewport === 'function') renderer.fitViewport();
                    setFlowMsg('<span class="text-gray-500">Edit the graph, then Validate or Save flow to scenario. Selected nodes show a <strong>yellow ring</strong>.</span>');
                });
            });
        }

        if (routerModeEl) routerModeEl.addEventListener('change', persistFlowRouterInspector);
        if (routerMaxEl) routerMaxEl.addEventListener('change', persistFlowRouterInspector);
        if (routerMinConfEl) routerMinConfEl.addEventListener('change', persistFlowRouterInspector);
        if (routerFallbackEl) routerFallbackEl.addEventListener('change', persistFlowRouterInspector);
        if (personaSelect) personaSelect.addEventListener('change', persistFlowPersonaInspector);

        openBtn.addEventListener('click', openModal);
        modal.addEventListener('click', function (e) {
            if (e.target.matches('[data-sc-flow-close]') || e.target.matches('[data-sc-flow-backdrop]')) closeModal();
        });

        modal.addEventListener('click', function (e) {
            var t = e.target;
            if (!t || !t.getAttribute) return;
            var add = t.getAttribute('data-flow-add');
            if (add && renderer) {
                var cfg = {};
                if (add === 'LlmNode') {
                    var s2 = currentScenario();
                    var ids = (s2 && s2.personaAgentIds) || [];
                    if (!ids.length) {
                        setFlowMsg('<span class="text-amber-700 dark:text-amber-300">No YAML personas on this scenario — add persona chips on the form, then assign each LlmNode node.</span>');
                        cfg.personaId = '';
                    } else {
                        cfg.personaId = normalizeType(ids[0]);
                    }
                }
                renderer.addNode(add, add, cfg);
                refreshFlowInspectors();
            }
        });

        btnConnect.addEventListener('click', function () {
            if (!renderer) return;
            var ok = renderer.connectSelected('sequential');
            setFlowMsg(ok ? '<span class="text-emerald-600">Connected selected nodes.</span>' : '<span class="text-amber-700">Select exactly two nodes (box-select).</span>');
        });

        if (btnDeleteEdges) {
            btnDeleteEdges.addEventListener('click', function () {
                if (!renderer || typeof renderer.removeSelectedEdges !== 'function') return;
                var n = renderer.removeSelectedEdges();
                setFlowMsg(n > 0
                    ? '<span class="text-emerald-600">Removed ' + n + ' edge(s).</span>'
                    : '<span class="text-amber-700">Select one or more edges (click the arrow), then delete.</span>');
                refreshFlowInspectors();
            });
        }

        modal.addEventListener('keydown', function (e) {
            if (modal.classList.contains('hidden') || !renderer) return;
            var tag = (e.target && e.target.tagName) ? e.target.tagName.toUpperCase() : '';
            if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA' || tag === 'BUTTON') return;
            if (e.key !== 'Delete' && e.key !== 'Backspace') return;
            if (typeof renderer.removeSelectedEdges !== 'function') return;
            var n = renderer.removeSelectedEdges();
            if (n > 0) {
                e.preventDefault();
                setFlowMsg('<span class="text-emerald-600">Removed ' + n + ' edge(s).</span>');
                refreshFlowInspectors();
            }
        });

        btnValidate.addEventListener('click', function () {
            if (!renderer || !draftBase) return;
            var doc = renderer.read(JSON.parse(JSON.stringify(draftBase)));
            var sc = currentScenario();
            var roster = (sc && sc.personaAgentIds) ? sc.personaAgentIds.slice() : [];
            var v = window.AgctorScenarioFlow.validateFlowDocument(doc, { personaAgentIds: roster });
            setFlowMsg(v.ok ? '<span class="text-emerald-600">Client checks OK (server validates on catalog save).</span>' : '<span class="text-red-600">' + esc(v.errors.join('; ')) + '</span>');
        });

        btnSimulate.addEventListener('click', function () {
            if (!renderer || !draftBase) return;
            var doc = renderer.read(JSON.parse(JSON.stringify(draftBase)));
            var sim = window.AgctorScenarioFlow.simulateOrder(doc);
            var llmNote = (sim.ok && flowDocHasLlmRouter(doc))
                ? ' <span class="text-amber-700 dark:text-amber-400">(LLM routers: order is illustrative only.)</span>'
                : '';
            setFlowMsg(sim.ok ? '<span class="text-gray-700 dark:text-gray-300">Traversal order: <strong>' + esc(sim.order.join(' → ')) + '</strong></span>' + llmNote : '<span class="text-red-600">' + esc(sim.errors.join('; ')) + '</span>');
        });

        btnSaveFlow.addEventListener('click', function () {
            var s = currentScenario();
            if (!s || !renderer || !draftBase) {
                setFlowMsg('<span class="text-amber-700">Nothing to save (open the flow editor first).</span>');
                return;
            }
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) {
                setFlowMsg('<span class="text-red-600">Graph is not ready yet — wait a second after opening, then try again.</span>');
                return;
            }
            persistFlowRouterInspector();
            persistFlowPersonaInspector();
            var doc = renderer.read(JSON.parse(JSON.stringify(draftBase)));
            setFlowMsg('<span class="text-gray-600 dark:text-gray-400">Saving to disk…</span>');
            btnSaveFlow.disabled = true;
            api('/api/scenarios/' + encodeURIComponent(s.id) + '/flow', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(doc)
            })
                .then(function () {
                    return loadCatalog();
                })
                .then(function () {
                    closeModal();
                    status.textContent = 'Flow saved to catalog file for scenario "' + String(s.id) + '".';
                })
                .catch(function (e) {
                    var m = (e && e.message) ? String(e.message) : 'Save failed';
                    setFlowMsg('<span class="text-red-600">' + esc(m) + '</span>');
                })
                .finally(function () {
                    btnSaveFlow.disabled = false;
                });
        });
    })();

    loadAll();
})();

