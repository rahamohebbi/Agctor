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
    var kindHelpEl = document.getElementById('sc-kind-help');
    var handlerSectionEl = document.getElementById('sc-handler-section');
    var handlerHelpEl = document.getElementById('sc-handler-help');
    var declarativeHintEl = document.getElementById('sc-declarative-hint');
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

    /** Plain-language labels for scenario kind (technical value stays in sc-kind input). */
    var KIND_LABELS = {
        declarative: 'Custom',
        scripted: 'Built-in demo'
    };

    var KIND_HELP = {
        declarative: 'You choose which agents start, which AI memory profiles to use, and how chat steps run in the flow designer.',
        scripted: 'A pre-installed example. A fixed setup program connects agents for you; you usually only need Apply to try it.'
    };

    var SCRIPTED_HANDLER_BLURBS = {
        CodeGenerationChainScenario: 'Sets up a root agent and an AI agent for generating and checking code.',
        CodeGraphDemoScenario: 'Sets up a small code-graph demo with indexing agents.'
    };

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
    /** From GET /api/Config → tools (id, name, description) for LlmNode extra tool picker. */
    var hostCatalogTools = [];

    /** Cached GET /api/tools/for-persona/{id} — eligibility from server (AgctorPersonaToolsUi). */
    var personaToolsById = {};

    function flowEligibleHostToolIds(personaId) {
        var dto = personaToolsById[normPersonaKey(personaId)];
        if (window.AgctorPersonaToolsUi && dto) {
            return window.AgctorPersonaToolsUi.eligibleHostToolIds(dto);
        }
        return [];
    }

    function normPersonaKey(personaId) {
        return String(personaId || '').trim().toLowerCase();
    }

    function ensurePersonaTools(personaId) {
        var key = normPersonaKey(personaId);
        if (!key || !window.AgctorPersonaToolsUi) return Promise.resolve(null);
        if (personaToolsById[key]) return Promise.resolve(personaToolsById[key]);
        return window.AgctorPersonaToolsUi.fetchForPersona(personaId).then(function (dto) {
            if (dto) personaToolsById[key] = dto;
            return dto;
        });
    }

    function sanitizeLlmNodeToolIdsForPersona(personaId, toolIds) {
        var eligible = flowEligibleHostToolIds(personaId);
        var arr = Array.isArray(toolIds) ? toolIds.map(function (x) { return String(x).toLowerCase(); }) : [];
        if (!eligible.length) return [];
        return arr.filter(function (x) {
            return eligible.indexOf(x) >= 0;
        });
    }

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
            var listKind = KIND_LABELS[String(s.kind || '').toLowerCase()] || s.kind;
            html += '<div class="text-xs text-gray-500 dark:text-gray-400">' + esc(s.id) + ' · ' + esc(listKind) + '</div>';
            html += '<div class="text-[11px] mt-1 text-gray-500 dark:text-gray-400">' + ((s.agentTypes || []).length) + ' agent(s)' + (getScenarioFlow(s) ? ' · conversation flow' : '') + '</div>';
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

    function isScriptedScenario(s) {
        return String((s && s.kind) || '').toLowerCase() === 'scripted';
    }

    function renderScenarioTypeHelp(s) {
        if (!s) return;
        var kind = String(s.kind || '').toLowerCase();
        var friendly = KIND_LABELS[kind] || (s.kind || 'unknown');
        var helpText = KIND_HELP[kind] || ('Technical type: ' + (s.kind || 'unknown'));
        if (kindHelpEl) {
            kindHelpEl.textContent = helpText;
        }
        if (kindEl) {
            kindEl.value = friendly;
            kindEl.title = helpText + ' (stored as: ' + (s.kind || '') + ')';
        }
        var scripted = isScriptedScenario(s);
        if (handlerSectionEl) {
            handlerSectionEl.classList.toggle('hidden', !scripted);
        }
        if (declarativeHintEl) {
            declarativeHintEl.classList.toggle('hidden', scripted);
        }
        if (handlerHelpEl) {
            if (!scripted) {
                handlerHelpEl.textContent = '';
            } else {
                var handlerName = String(s.handler || '').trim();
                var blurb = SCRIPTED_HANDLER_BLURBS[handlerName] || '';
                if (!handlerName) {
                    handlerHelpEl.textContent = 'This demo scenario is missing a setup program name in config. It may not run until a developer fixes it.';
                } else if (blurb) {
                    handlerHelpEl.textContent = 'Program name: ' + handlerName + '. ' + blurb + ' You cannot change this here.';
                } else {
                    handlerHelpEl.textContent = 'Program name: ' + handlerName + '. This is a developer-maintained demo; agent and profile lists below are mainly for reference.';
                }
            }
        }
        if (handlerEl) {
            handlerEl.value = s.handler || '';
            handlerEl.placeholder = scripted ? '' : 'Not used for custom scenarios';
        }
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
        displayEl.value = s.displayName || '';
        descEl.value = s.description || '';
        typesEl.value = (s.agentTypes || []).join('\n');
        bindExtractor.value = (s.personaBindings && s.personaBindings.extractor) || '';
        bindCurator.value = (s.personaBindings && s.personaBindings.curator) || '';
        bindQuery.value = (s.personaBindings && s.personaBindings.query) || '';
        renderScenarioTypeHelp(s);
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
            chipsEl.innerHTML = '<span class="text-xs text-gray-400 dark:text-gray-500">No agents selected — add at least one if you want something to start when you apply this scenario.</span>';
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
            'When you click Apply: <strong>Will start now:</strong> ' + esc(willStart.length ? willStart.join(', ') : '(none)') +
            ' · <strong>Saved for this scenario only:</strong> ' + esc(configOnly.length ? configOnly.join(', ') : '(none)');

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
                var toolsHint = '';
                var key = normPersonaKey(p);
                var dto = personaToolsById[key];
                if (dto && window.AgctorPersonaToolsUi) {
                    var summary = window.AgctorPersonaToolsUi.summarizeAllowedHostTools(dto);
                    if (summary) {
                        toolsHint =
                            ' <span class="ml-1 font-normal opacity-80" title="' +
                            esc(window.AgctorPersonaToolsUi.summarizeToolsTitle(dto)) +
                            '">· ' +
                            esc(summary) +
                            '</span>';
                    }
                }
                personaChipsEl.innerHTML +=
                    '<span class="inline-flex items-center gap-1 rounded px-2 py-1 text-xs font-medium ' + cls + '" title="' + esc(title) + '" data-persona-chip="' + esc(p) + '">' +
                    esc(p) +
                    toolsHint +
                    '<button type="button" class="font-bold leading-none hover:opacity-80" data-remove-persona="' + esc(p) + '" aria-label="Remove ' + esc(p) + '">×</button>' +
                    '</span>';
                if (known && window.AgctorPersonaToolsUi && !dto) {
                    ensurePersonaTools(p).then(function () {
                        renderPersonaEditor();
                    }).catch(function () { /* chip stays without tools hint */ });
                }
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
            hostCatalogTools = Array.isArray(cfg.tools) ? cfg.tools.slice() : [];
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

    var listStatus = document.getElementById('sc-list-status');
    function setListStatus(msg) {
        if (listStatus) listStatus.textContent = msg || '';
    }

    /** Inline modal — window.prompt is blocked in many embedded browsers. */
    var newScenarioBtn = document.getElementById('sc-new-scenario');
    var newModal = document.getElementById('sc-new-modal');
    var newIdInput = document.getElementById('sc-new-id');
    var newDisplayInput = document.getElementById('sc-new-display');
    var newErrorEl = document.getElementById('sc-new-error');
    var newSubmitBtn = document.getElementById('sc-new-submit');

    function showNewError(msg) {
        if (!newErrorEl) return;
        if (msg) {
            newErrorEl.textContent = msg;
            newErrorEl.classList.remove('hidden');
        } else {
            newErrorEl.textContent = '';
            newErrorEl.classList.add('hidden');
        }
    }

    function closeNewModal() {
        if (newModal) newModal.classList.add('hidden');
        showNewError('');
    }

    function openNewModal() {
        if (!newModal || !newIdInput) return;
        newIdInput.value = '';
        if (newDisplayInput) newDisplayInput.value = '';
        showNewError('');
        newModal.classList.remove('hidden');
        newIdInput.focus();
    }

    function isValidNewScenarioId(id) {
        return /^[A-Za-z0-9._-]{1,120}$/.test(id);
    }

    function submitNewScenario() {
        if (!newIdInput) return;
        var id = String(newIdInput.value || '').trim();
        var disp = newDisplayInput ? String(newDisplayInput.value || '').trim() : '';
        if (!id) {
            showNewError('Id is required.');
            newIdInput.focus();
            return;
        }
        if (!isValidNewScenarioId(id)) {
            showNewError('Id must be 1–120 characters: letters, digits, hyphen, underscore, or dot.');
            newIdInput.focus();
            return;
        }
        disp = disp || id;
        showNewError('');
        setListStatus('Creating…');
        if (newSubmitBtn) newSubmitBtn.disabled = true;
        if (newScenarioBtn) newScenarioBtn.disabled = true;
        api('/api/scenarios', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ id: id, displayName: disp, description: '' })
        })
            .then(function (created) {
                selectedId = (created && created.id) || id;
                closeNewModal();
                setListStatus('Created "' + selectedId + '".');
                status.textContent = '';
                return loadCatalog();
            })
            .catch(function (e) {
                showNewError(e.message || 'Create failed');
                setListStatus('');
            })
            .finally(function () {
                if (newSubmitBtn) newSubmitBtn.disabled = false;
                if (newScenarioBtn) newScenarioBtn.disabled = false;
            });
    }

    if (newScenarioBtn) {
        newScenarioBtn.addEventListener('click', openNewModal);
    }
    if (newSubmitBtn) {
        newSubmitBtn.addEventListener('click', submitNewScenario);
    }
    if (newIdInput) {
        newIdInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                submitNewScenario();
            }
        });
    }
    if (newDisplayInput) {
        newDisplayInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                submitNewScenario();
            }
        });
    }
    if (newModal) {
        newModal.querySelectorAll('[data-sc-new-cancel], [data-sc-new-backdrop]').forEach(function (el) {
            el.addEventListener('click', closeNewModal);
        });
        newModal.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && !newModal.classList.contains('hidden')) closeNewModal();
        });
    }

    var delScenarioBtn = document.getElementById('sc-delete-scenario');
    if (delScenarioBtn) {
        delScenarioBtn.addEventListener('click', function () {
            if (!selectedId) {
                setListStatus('Select a scenario to delete.');
                return;
            }
            if (!window.confirm('Delete scenario "' + selectedId + '" from the user catalog? Built-in defaults require a second delete to hide.')) return;
            setListStatus('Deleting…');
            status.textContent = '';
            delScenarioBtn.disabled = true;
            api('/api/scenarios/' + encodeURIComponent(selectedId), { method: 'DELETE' })
                .then(function () {
                    setListStatus('Deleted.');
                    selectedId = null;
                    return loadCatalog();
                })
                .catch(function (e) {
                    setListStatus(e.message || 'Delete failed');
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
        var btnCopyFlow = document.getElementById('sc-flow-copy');
        var copyModal = document.getElementById('sc-flow-copy-modal');
        var copyTargetEl = document.getElementById('sc-flow-copy-target');
        var copyErrorEl = document.getElementById('sc-flow-copy-error');
        var copyStepSelect = document.getElementById('sc-flow-copy-step-select');
        var copyStepConfirm = document.getElementById('sc-flow-copy-step-confirm');
        var copyConfirmText = document.getElementById('sc-flow-copy-confirm-text');
        var copyApproveBtn = document.getElementById('sc-flow-copy-approve');
        var copyOverrideBtn = document.getElementById('sc-flow-copy-override');
        var btnConnect = document.getElementById('sc-flow-connect');
        var btnConnectLoop = document.getElementById('sc-flow-connect-loop');
        var btnDeleteEdges = document.getElementById('sc-flow-delete-edges');
        var schemaBadge = document.getElementById('sc-flow-schema-badge');
        var showLoopRegionsEl = document.getElementById('sc-flow-show-loop-regions');
        var gatePanel = document.getElementById('sc-flow-gate-panel');
        var gateFactEl = document.getElementById('sc-flow-gate-fact');
        var gateOpEl = document.getElementById('sc-flow-gate-operator');
        var gateTrueEdgeEl = document.getElementById('sc-flow-gate-true-edge');
        var gateFalseEdgeEl = document.getElementById('sc-flow-gate-false-edge');
        var waitPanel = document.getElementById('sc-flow-wait-panel');
        var waitPromptEl = document.getElementById('sc-flow-wait-prompt');
        var waitAttachmentsEl = document.getElementById('sc-flow-wait-attachments');
        var waitPolicyEl = document.getElementById('sc-flow-wait-policy');
        var awaitPanel = document.getElementById('sc-flow-await-panel');
        var awaitEventEl = document.getElementById('sc-flow-await-event');
        var awaitTimeoutEl = document.getElementById('sc-flow-await-timeout');
        var notifyPanel = document.getElementById('sc-flow-notify-panel');
        var notifyTargetEl = document.getElementById('sc-flow-notify-target');
        var notifySignalEl = document.getElementById('sc-flow-notify-signal');
        var execSummary = document.getElementById('sc-flow-exec-summary');
        var execStatusEl = document.getElementById('sc-flow-exec-status');
        var execNodeEl = document.getElementById('sc-flow-exec-node');
        var execPromptEl = document.getElementById('sc-flow-exec-prompt');
        var simulateTurnsBar = document.getElementById('sc-flow-simulate-turns');
        var btnSimulateTurns = document.getElementById('sc-flow-simulate-turns-btn');
        var turnMessageEl = document.getElementById('sc-flow-turn-message');
        var turnAttachmentsEl = document.getElementById('sc-flow-turn-attachments');
        var btnTurnRun = document.getElementById('sc-flow-turn-run');
        var btnEventRun = document.getElementById('sc-flow-event-run');
        var btnTurnReset = document.getElementById('sc-flow-turn-reset');
        var edgeLoopBlock = document.getElementById('sc-flow-edge-loop-block');
        var edgeConvertLoopBtn = document.getElementById('sc-flow-edge-convert-loop');
        var loopRegionIdEl = document.getElementById('sc-flow-loop-region-id');
        var loopMaxEl = document.getElementById('sc-flow-loop-max');
        var loopInvalidationEl = document.getElementById('sc-flow-loop-invalidation');
        var routerPanel = document.getElementById('sc-flow-router-panel');
        var routerModeEl = document.getElementById('sc-flow-router-mode');
        var routerTargetPolicyEl = document.getElementById('sc-flow-router-target-policy');
        var routerBranchExecEl = document.getElementById('sc-flow-router-branch-exec');
        var routerBranchExecWrap = document.getElementById('sc-flow-router-branch-exec-wrap');
        var routerMaxEl = document.getElementById('sc-flow-router-max');
        var routerMinConfEl = document.getElementById('sc-flow-router-minconf');
        var routerFallbackEl = document.getElementById('sc-flow-router-fallback');
        var routerCandUl = document.getElementById('sc-flow-router-candidates');
        var routerLlmInstrEl = document.getElementById('sc-flow-router-llm-instr');
        var edgePanel = document.getElementById('sc-flow-edge-panel');
        var edgeMetaEl = document.getElementById('sc-flow-edge-meta');
        var edgeConditionEl = document.getElementById('sc-flow-edge-condition');
        var edgeMatchEl = document.getElementById('sc-flow-edge-match');
        var edgeLlmHintEl = document.getElementById('sc-flow-edge-llm-hint');
        var personaPanel = document.getElementById('sc-flow-persona-panel');
        var personaSelect = document.getElementById('sc-flow-persona-select');
        var pqContextWrap = document.getElementById('sc-flow-pq-context-wrap');
        var pqContextStrategyEl = document.getElementById('sc-flow-pq-context-strategy');
        var personaRosterHint = document.getElementById('sc-flow-persona-roster-hint');
        var personaInvalidHint = document.getElementById('sc-flow-persona-invalid-hint');
        var personaCapEl = document.getElementById('sc-flow-persona-cap');
        var flowAgentToolsSection = document.getElementById('sc-flow-agent-tools-section');
        var flowYamlToolsEl = document.getElementById('sc-flow-yaml-tools');
        var flowEditAgentLink = document.getElementById('sc-flow-edit-agent-link');
        var flowLlmToolsEl = document.getElementById('sc-flow-llm-tools');
        if (!openBtn || !modal || !cyHost || !msgEl || !btnValidate || !btnSimulate || !btnSaveFlow || !btnConnect) return;
        if (!window.AgctorScenarioFlow || typeof window.AgctorScenarioFlow.createGraphRenderer !== 'function') return;

        var renderer = null;
        var draftBase = null;
        /** Client-side multi-turn simulate state (PRD-024). */
        var flowSimState = null;
        /** Target scenario id when override step is shown (copy flow modal). */
        var copyPendingTargetId = null;

        function readFlowDocFromRenderer() {
            if (!renderer || !draftBase) return null;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) return null;
            persistFlowRouterInspector();
            persistFlowPersonaInspector();
            persistFlowEdgeInspector();
            persistFlowV2NodeInspectors();
            return renderer.read(JSON.parse(JSON.stringify(draftBase)));
        }

        function hideAllV2Panels() {
            if (gatePanel) gatePanel.classList.add('hidden');
            if (waitPanel) waitPanel.classList.add('hidden');
            if (awaitPanel) awaitPanel.classList.add('hidden');
            if (notifyPanel) notifyPanel.classList.add('hidden');
        }

        function populateGateFactOptions(selected) {
            if (!gateFactEl) return;
            var facts = (window.AgctorScenarioFlow && window.AgctorScenarioFlow.knownFacts) || [];
            gateFactEl.innerHTML = facts.map(function (f) {
                return '<option value="' + esc(f) + '">' + esc(f) + '</option>';
            }).join('');
            if (selected) gateFactEl.value = selected;
        }

        function populateGateOperatorOptions(selected) {
            if (!gateOpEl) return;
            var ops = (window.AgctorScenarioFlow && window.AgctorScenarioFlow.gateOperators) || [];
            gateOpEl.innerHTML = ops.map(function (o) {
                return '<option value="' + esc(o) + '">' + esc(o) + '</option>';
            }).join('');
            if (selected) gateOpEl.value = selected;
        }

        function populateAwaitEventOptions(selected) {
            if (!awaitEventEl) return;
            var evs = (window.AgctorScenarioFlow && window.AgctorScenarioFlow.knownEvents) || [];
            awaitEventEl.innerHTML = evs.map(function (ev) {
                return '<option value="' + esc(ev) + '">' + esc(ev) + '</option>';
            }).join('');
            if (selected) awaitEventEl.value = selected;
        }

        function gateEdgeOptions(doc, gateNodeId, selectedTrue, selectedFalse) {
            if (!gateTrueEdgeEl || !gateFalseEdgeEl) return;
            var edges = (doc.edges || []).filter(function (e) {
                return e && e.fromNodeId === gateNodeId;
            });
            var opts = edges.map(function (e) {
                var tn = (doc.nodes || []).filter(function (n) { return n && n.id === e.toNodeId; })[0];
                var label = e.id + ' → ' + (tn ? tn.label || tn.id : e.toNodeId);
                return '<option value="' + esc(e.id) + '">' + esc(label) + '</option>';
            }).join('');
            gateTrueEdgeEl.innerHTML = opts || '<option value="">(connect gate first)</option>';
            gateFalseEdgeEl.innerHTML = opts || '<option value="">(connect gate first)</option>';
            if (selectedTrue) gateTrueEdgeEl.value = selectedTrue;
            if (selectedFalse) gateFalseEdgeEl.value = selectedFalse;
        }

        function refreshFlowV2NodeInspectors() {
            hideAllV2Panels();
            if (!renderer) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) return;
            var sel = cy.$('node:selected');
            if (sel.length !== 1) return;
            var type = String(sel.data('agctorType') || '');
            var cfg = {};
            try {
                cfg = JSON.parse(sel.data('agctorConfig') || '{}');
            } catch (e) {
                cfg = {};
            }
            var doc = renderer.read(JSON.parse(JSON.stringify(draftBase)));
            if (type === 'Gate' && gatePanel) {
                gatePanel.classList.remove('hidden');
                populateGateFactOptions(cfg.fact || 'visual.hasPhotos');
                populateGateOperatorOptions(cfg.operator || 'isFalse');
                gateEdgeOptions(doc, sel.id(), cfg.trueEdgeId, cfg.falseEdgeId);
            } else if (type === 'WaitForInput' && waitPanel) {
                waitPanel.classList.remove('hidden');
                if (waitPromptEl) waitPromptEl.value = cfg.promptTemplate || '';
                if (waitAttachmentsEl) waitAttachmentsEl.checked = cfg.acceptAttachments !== false;
                if (waitPolicyEl) waitPolicyEl.value = cfg.attachmentPolicy || 'imagesOnly';
            } else if (type === 'AwaitEvent' && awaitPanel) {
                awaitPanel.classList.remove('hidden');
                populateAwaitEventOptions(cfg.eventType || 'visual.extract.completed');
                if (awaitTimeoutEl) awaitTimeoutEl.value = cfg.timeoutSeconds != null ? String(cfg.timeoutSeconds) : '120';
            } else if (type === 'Notify' && notifyPanel) {
                notifyPanel.classList.remove('hidden');
                if (notifyTargetEl) notifyTargetEl.value = cfg.target || '';
                if (notifySignalEl) notifySignalEl.value = cfg.signal || '';
            }
        }

        function persistFlowV2NodeInspectors() {
            if (!renderer) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) return;
            var n = cy.$('node:selected');
            if (n.length !== 1) return;
            var type = String(n.data('agctorType') || '');
            var cfg = {};
            try {
                cfg = JSON.parse(n.data('agctorConfig') || '{}');
            } catch (e) {
                cfg = {};
            }
            if (type === 'Gate') {
                if (gateFactEl) cfg.fact = gateFactEl.value;
                if (gateOpEl) cfg.operator = gateOpEl.value;
                if (gateTrueEdgeEl) cfg.trueEdgeId = gateTrueEdgeEl.value;
                if (gateFalseEdgeEl) cfg.falseEdgeId = gateFalseEdgeEl.value;
            } else if (type === 'WaitForInput') {
                if (waitPromptEl) cfg.promptTemplate = waitPromptEl.value;
                if (waitAttachmentsEl) cfg.acceptAttachments = !!waitAttachmentsEl.checked;
                if (waitPolicyEl) cfg.attachmentPolicy = waitPolicyEl.value || 'imagesOnly';
            } else if (type === 'AwaitEvent') {
                if (awaitEventEl) cfg.eventType = awaitEventEl.value;
                if (awaitTimeoutEl) {
                    var ts = parseInt(awaitTimeoutEl.value, 10);
                    cfg.timeoutSeconds = !isNaN(ts) && ts > 0 ? ts : 120;
                }
            } else if (type === 'Notify') {
                if (notifyTargetEl) cfg.target = notifyTargetEl.value.trim();
                if (notifySignalEl) cfg.signal = notifySignalEl.value.trim();
            } else {
                return;
            }
            n.data('agctorConfig', JSON.stringify(cfg));
        }

        function updateEdgeRouteCaption(e) {
            if (!e || !window.AgctorScenarioFlow || typeof window.AgctorScenarioFlow.edgeRouteCaption !== 'function') return;
            var lc = {};
            try {
                lc = JSON.parse(e.data('loopConfig') || '{}');
            } catch (err) {
                lc = {};
            }
            e.data(
                'routeCaption',
                window.AgctorScenarioFlow.edgeRouteCaption({
                    mode: e.data('mode') || 'sequential',
                    condition: e.data('condition') || '',
                    conditionMatch: e.data('conditionMatch') || 'contains',
                    llmRoutingHint: e.data('llmRoutingHint') || '',
                    loopConfig: lc
                })
            );
        }

        function refreshLoopRegionOverlay() {
            if (!renderer || typeof renderer.setLoopRegionOverlay !== 'function') return;
            var on = showLoopRegionsEl && showLoopRegionsEl.checked;
            renderer.setLoopRegionOverlay(!!on);
        }

        function updateV2StudioUi() {
            if (!renderer || !draftBase) return;
            var doc = renderer.read(JSON.parse(JSON.stringify(draftBase)));
            var v2 = window.AgctorScenarioFlow && typeof window.AgctorScenarioFlow.isV2Flow === 'function'
                ? window.AgctorScenarioFlow.isV2Flow(doc)
                : false;
            if (schemaBadge) {
                if (v2) {
                    schemaBadge.classList.remove('hidden');
                    schemaBadge.textContent = 'Schema v2.0 — runtime actor, loop-back, suspend/resume';
                } else {
                    schemaBadge.classList.add('hidden');
                    schemaBadge.textContent = '';
                }
            }
            if (btnSimulateTurns) btnSimulateTurns.classList.toggle('hidden', !v2);
            if (simulateTurnsBar) simulateTurnsBar.classList.toggle('hidden', !v2);
            refreshLoopRegionOverlay();
        }

        function resetFlowSimState() {
            flowSimState = null;
            if (execSummary) execSummary.classList.add('hidden');
            if (execStatusEl) execStatusEl.textContent = '';
            if (execNodeEl) execNodeEl.textContent = '';
            if (execPromptEl) {
                execPromptEl.classList.add('hidden');
                execPromptEl.textContent = '';
            }
            if (renderer && typeof renderer.setExecutionHighlight === 'function') {
                renderer.setExecutionHighlight(null);
            }
        }

        function renderExecSummary(state) {
            if (!execSummary || !state) return;
            execSummary.classList.remove('hidden');
            if (execStatusEl) execStatusEl.textContent = 'Status: ' + String(state.status || '—');
            if (execNodeEl) execNodeEl.textContent = 'At node: ' + String(state.executionNodeId || '—');
            if (execPromptEl) {
                if (state.pendingPrompt) {
                    execPromptEl.classList.remove('hidden');
                    execPromptEl.textContent = 'Pending: ' + String(state.pendingPrompt);
                } else if (state.awaitingEvent) {
                    execPromptEl.classList.remove('hidden');
                    execPromptEl.textContent = 'Awaiting event: ' + String(state.awaitingEvent);
                } else {
                    execPromptEl.classList.add('hidden');
                    execPromptEl.textContent = '';
                }
            }
            if (renderer && typeof renderer.setExecutionHighlight === 'function') {
                renderer.setExecutionHighlight(state.executionNodeId || null);
            }
        }

        function runSimulateTurn(turnInput) {
            if (!renderer || !draftBase) return;
            var doc = renderer.read(JSON.parse(JSON.stringify(draftBase)));
            if (!window.AgctorScenarioFlow || typeof window.AgctorScenarioFlow.simulateTurn !== 'function') return;
            var result = window.AgctorScenarioFlow.simulateTurn(doc, turnInput || {}, flowSimState);
            if (!result.ok) {
                setFlowMsg('<span class="text-red-600">' + esc(result.errors.join('; ')) + '</span>');
                if (result.state) {
                    flowSimState = result.state;
                    renderExecSummary(flowSimState);
                }
                return;
            }
            flowSimState = result.state;
            renderExecSummary(flowSimState);
            var steps = (flowSimState.steps || []).slice(-6);
            var chips = steps.map(function (s, i) {
                return esc(s.label || s.id) + (s.type ? ' (' + s.type + ')' : '');
            }).join(' → ');
            var done = flowSimState.completed
                ? ' <span class="text-emerald-600">Completed.</span>'
                : '';
            setFlowMsg('<span class="text-gray-700 dark:text-gray-300">Turn trace: ' + chips + done + '</span>');
        }

        function flowDocForTargetScenario(doc, targetScenarioId) {
            var copy = JSON.parse(JSON.stringify(doc));
            var tid = String(targetScenarioId || '').trim();
            copy.graphId = tid + '-flow';
            return copy;
        }

        function scenarioById(id) {
            return all.find(function (x) { return x.id === id; }) || null;
        }

        function targetScenarioHasSavedFlow(targetId) {
            var t = scenarioById(targetId);
            return !!getScenarioFlow(t);
        }

        function resetCopyFlowModal() {
            copyPendingTargetId = null;
            if (copyStepSelect) copyStepSelect.classList.remove('hidden');
            if (copyStepConfirm) copyStepConfirm.classList.add('hidden');
            if (copyApproveBtn) {
                copyApproveBtn.classList.remove('hidden');
                copyApproveBtn.disabled = false;
                copyApproveBtn.textContent = 'Approve';
            }
            if (copyOverrideBtn) copyOverrideBtn.classList.add('hidden');
            if (copyErrorEl) {
                copyErrorEl.classList.add('hidden');
                copyErrorEl.textContent = '';
            }
        }

        function closeCopyFlowModal() {
            if (!copyModal) return;
            copyModal.classList.add('hidden');
            resetCopyFlowModal();
        }

        function populateCopyTargetDropdown(sourceId) {
            if (!copyTargetEl) return;
            var opts = '<option value="">— Select a scenario —</option>';
            for (var i = 0; i < all.length; i++) {
                var sc = all[i];
                if (!sc || !sc.id || sc.id === sourceId) continue;
                var label = String(sc.displayName || sc.id).trim();
                if (label !== sc.id) label += ' (' + sc.id + ')';
                if (getScenarioFlow(sc)) label += ' · has flow';
                opts += '<option value="' + esc(sc.id) + '">' + esc(label) + '</option>';
            }
            copyTargetEl.innerHTML = opts;
            copyTargetEl.value = '';
        }

        function openCopyFlowModal() {
            var s = currentScenario();
            if (!s || !copyModal) return;
            if (!readFlowDocFromRenderer()) {
                setFlowMsg('<span class="text-amber-700">Open the flow designer and wait for the canvas to load before copying.</span>');
                return;
            }
            resetCopyFlowModal();
            populateCopyTargetDropdown(s.id);
            copyModal.classList.remove('hidden');
            if (copyTargetEl) copyTargetEl.focus();
        }

        function showCopyOverrideStep(targetId) {
            var t = scenarioById(targetId);
            var tLabel = t ? (t.displayName || t.id) : targetId;
            copyPendingTargetId = targetId;
            if (copyStepSelect) copyStepSelect.classList.add('hidden');
            if (copyStepConfirm) copyStepConfirm.classList.remove('hidden');
            if (copyConfirmText) {
                copyConfirmText.textContent =
                    '“' + tLabel + '” (' + targetId + ') already has a saved conversation flow. Do you want to replace it with the flow from this canvas?';
            }
            if (copyApproveBtn) copyApproveBtn.classList.add('hidden');
            if (copyOverrideBtn) copyOverrideBtn.classList.remove('hidden');
        }

        function performCopyFlowToTarget(targetId) {
            var source = currentScenario();
            if (!source) return Promise.reject(new Error('No source scenario selected.'));
            var doc = readFlowDocFromRenderer();
            if (!doc) return Promise.reject(new Error('Flow canvas is not ready.'));
            var payload = flowDocForTargetScenario(doc, targetId);
            var target = scenarioById(targetId);
            var roster = (target && target.personaAgentIds) ? target.personaAgentIds.slice() : [];
            if (window.AgctorScenarioFlow && typeof window.AgctorScenarioFlow.validateFlowDocument === 'function') {
                var v = window.AgctorScenarioFlow.validateFlowDocument(payload, { personaAgentIds: roster });
                if (!v.ok) {
                    return Promise.reject(new Error('Flow is not valid for the target scenario: ' + v.errors.join('; ')));
                }
            }
            if (copyApproveBtn) copyApproveBtn.disabled = true;
            if (copyOverrideBtn) copyOverrideBtn.disabled = true;
            return api('/api/scenarios/' + encodeURIComponent(targetId) + '/flow', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            }).then(function () {
                return loadCatalog();
            }).then(function () {
                closeCopyFlowModal();
                var tLabel = target ? (target.displayName || target.id) : targetId;
                setFlowMsg('<span class="text-emerald-600">Flow copied to “' + esc(tLabel) + '”.</span>');
                status.textContent = 'Flow copied to scenario "' + targetId + '".';
            }).finally(function () {
                if (copyApproveBtn) copyApproveBtn.disabled = false;
                if (copyOverrideBtn) copyOverrideBtn.disabled = false;
            });
        }
        /** PRD-014 Phase 11: `id.toLowerCase()` → agent row from `GET /api/project-memory/agents`. */
        var flowModalAgentLabels = {};
        var traverseHoverCleanup = null;

        function clearTraverseHoverLink() {
            if (traverseHoverCleanup) {
                traverseHoverCleanup();
                traverseHoverCleanup = null;
            }
            if (renderer && typeof renderer.clearTraverseHighlight === 'function') {
                renderer.clearTraverseHighlight();
            }
        }

        function setFlowMsg(html) {
            clearTraverseHoverLink();
            msgEl.innerHTML = html || '';
        }

        /** Interactive traversal strip for Simulate order (node labels + hover link to canvas). */
        function buildTraverseOrderHtml(steps, llmNote) {
            var chips = (steps || []).map(function (s, i) {
                var tip = String(s.label || s.id) + ' · ' + String(s.id);
                if (s.type) tip += ' · ' + String(s.type);
                return '<span class="sc-flow-traverse-chip" data-node-id="' + esc(s.id) + '" title="' + esc(tip) + '">' +
                    '<span class="sc-flow-traverse-idx">' + (i + 1) + '</span>' +
                    esc(s.label || s.id) +
                    '</span>';
            }).join('<span class="sc-flow-traverse-arrow" aria-hidden="true">→</span>');
            return '<div class="sc-flow-traverse-wrap">' +
                '<span class="font-medium text-gray-800 dark:text-gray-200">Traversal order</span>' +
                '<div class="sc-flow-traverse-track">' + chips + '</div>' +
                (llmNote || '') +
                '<p class="mt-1 text-[10px] text-gray-500 dark:text-gray-400">Hover a step or a graph node to highlight the match.</p>' +
                '</div>';
        }

        function linkTraverseHover(steps) {
            clearTraverseHoverLink();
            if (!renderer || !steps || !steps.length) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) return;

            function setHover(nodeId) {
                if (!nodeId) return;
                if (typeof renderer.setTraverseHighlight === 'function') {
                    renderer.setTraverseHighlight(nodeId);
                }
                msgEl.querySelectorAll('.sc-flow-traverse-chip').forEach(function (chip) {
                    chip.classList.toggle('is-hover', chip.getAttribute('data-node-id') === nodeId);
                });
            }

            function clearHover() {
                if (typeof renderer.clearTraverseHighlight === 'function') {
                    renderer.clearTraverseHighlight();
                }
                msgEl.querySelectorAll('.sc-flow-traverse-chip.is-hover').forEach(function (chip) {
                    chip.classList.remove('is-hover');
                });
            }

            function onChipEnter(e) {
                setHover(e.currentTarget.getAttribute('data-node-id'));
            }

            var chips = msgEl.querySelectorAll('.sc-flow-traverse-chip');
            chips.forEach(function (chip) {
                chip.addEventListener('mouseenter', onChipEnter);
                chip.addEventListener('mouseleave', clearHover);
            });

            var cyHandlers = [];
            cy.nodes().forEach(function (n) {
                var nid = n.id();
                var onEnter = function () { setHover(nid); };
                var onLeave = function () { clearHover(); };
                n.on('mouseover', onEnter);
                n.on('mouseout', onLeave);
                cyHandlers.push({ n: n, onEnter: onEnter, onLeave: onLeave });
            });

            traverseHoverCleanup = function () {
                chips.forEach(function (chip) {
                    chip.removeEventListener('mouseenter', onChipEnter);
                    chip.removeEventListener('mouseleave', clearHover);
                });
                cyHandlers.forEach(function (h) {
                    h.n.off('mouseover', h.onEnter);
                    h.n.off('mouseout', h.onLeave);
                });
            };
        }

        function closeModal() {
            clearTraverseHoverLink();
            resetFlowSimState();
            if (renderer) {
                renderer.destroy();
                renderer = null;
            }
            draftBase = null;
            if (routerPanel) routerPanel.classList.add('hidden');
            if (personaPanel) personaPanel.classList.add('hidden');
            if (edgePanel) edgePanel.classList.add('hidden');
            hideAllV2Panels();
            if (personaCapEl) {
                personaCapEl.innerHTML = '';
                personaCapEl.classList.add('hidden');
            }
            if (flowAgentToolsSection) flowAgentToolsSection.classList.add('hidden');
            if (flowYamlToolsEl) flowYamlToolsEl.innerHTML = '';
            if (flowLlmToolsEl) flowLlmToolsEl.innerHTML = '';
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

        /** Canvas label for LlmNode — same display name as the persona dropdown (without id suffix). */
        function getLlmNodeCanvasLabel(personaId) {
            var pid = normalizeType(personaId);
            if (!pid) return 'LlmNode';
            return getFlowPersonaLabel(pid) || pid;
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
            if (!renderer) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) return;
            updateV2StudioUi();
            var esel = cy.$('edge:selected');
            if (esel.length === 1) {
                if (routerPanel) routerPanel.classList.add('hidden');
                if (personaPanel) personaPanel.classList.add('hidden');
                hideAllV2Panels();
                if (pqContextWrap) pqContextWrap.classList.add('hidden');
                if (flowAgentToolsSection) flowAgentToolsSection.classList.add('hidden');
                if (flowYamlToolsEl) flowYamlToolsEl.innerHTML = '';
                if (personaCapEl) {
                    personaCapEl.innerHTML = '';
                    personaCapEl.classList.add('hidden');
                }
                refreshFlowEdgeInspector();
                return;
            }
            if (edgePanel) edgePanel.classList.add('hidden');
            refreshFlowRouterInspector();
            refreshFlowPersonaInspector();
            refreshFlowV2NodeInspectors();
        }

        function refreshFlowEdgeInspector() {
            if (!renderer || !edgePanel || !edgeMetaEl || !edgeConditionEl || !edgeMatchEl || !edgeLlmHintEl) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) {
                edgePanel.classList.add('hidden');
                return;
            }
            var esel = cy.$('edge:selected');
            if (esel.length !== 1) {
                edgePanel.classList.add('hidden');
                return;
            }
            edgePanel.classList.remove('hidden');
            var e = esel[0];
            var edgeMode = String(e.data('mode') || 'sequential');
            edgeMetaEl.textContent = e.data('source') + ' → ' + e.data('target') + '   id:' + e.id() + '   mode:' + edgeMode;
            edgeConditionEl.value = String(e.data('condition') || '');
            edgeMatchEl.value = String(e.data('conditionMatch') || 'contains').toLowerCase();
            edgeLlmHintEl.value = String(e.data('llmRoutingHint') || '');

            var isLoop = edgeMode === 'loopBack';
            if (edgeLoopBlock) edgeLoopBlock.classList.toggle('hidden', !isLoop);
            if (isLoop && loopRegionIdEl && loopMaxEl && loopInvalidationEl) {
                var lc = {};
                try {
                    lc = JSON.parse(e.data('loopConfig') || '{}');
                } catch (err) {
                    lc = {};
                }
                loopRegionIdEl.value = lc.loopRegionId || '';
                loopMaxEl.value = lc.maxAttempts != null ? String(lc.maxAttempts) : '3';
                loopInvalidationEl.value = lc.storeInvalidation || 'fromTargetForward';
            }
            if (edgeConvertLoopBtn) {
                edgeConvertLoopBtn.textContent = isLoop ? 'Already loop back' : 'Convert to loop back';
                edgeConvertLoopBtn.disabled = isLoop;
            }

            var edgeCtx = document.getElementById('sc-flow-edge-router-context');
            var detBlock = document.getElementById('sc-flow-edge-det-block');
            var llmBlock = document.getElementById('sc-flow-edge-llm-block');
            var sid = String(e.data('source'));
            var src = cy.getElementById(sid);
            var fromRouter = src && src.length > 0 && String(src.data('agctorType') || '') === 'Router';
            var routerUsesLlm = false;
            if (fromRouter) {
                try {
                    var rc = JSON.parse(src.data('agctorConfig') || '{}');
                    routerUsesLlm = String(rc.routerMode || '').toLowerCase() === 'llm';
                } catch (err) {
                    routerUsesLlm = false;
                }
            }
            if (edgeCtx) {
                if (isLoop) {
                    edgeCtx.classList.remove('hidden');
                    edgeCtx.textContent = 'Loop-back edge: configure region id and max attempts below. Routing conditions are ignored.';
                } else if (fromRouter) {
                    edgeCtx.classList.remove('hidden');
                    edgeCtx.textContent = routerUsesLlm
                        ? 'This arrow leaves an LLM-mode router: describe when to take this branch (hint below). Conditions are hidden because they are not used in LLM mode.'
                        : 'This arrow leaves a deterministic router: set condition + match mode below. The LLM hint is hidden because it is only used when the router is in LLM mode.';
                } else {
                    edgeCtx.classList.add('hidden');
                    edgeCtx.textContent = '';
                }
            }
            if (detBlock && llmBlock) {
                if (isLoop) {
                    detBlock.classList.add('hidden');
                    llmBlock.classList.add('hidden');
                } else if (!fromRouter) {
                    detBlock.classList.remove('hidden');
                    llmBlock.classList.remove('hidden');
                } else if (routerUsesLlm) {
                    detBlock.classList.add('hidden');
                    llmBlock.classList.remove('hidden');
                } else {
                    detBlock.classList.remove('hidden');
                    llmBlock.classList.add('hidden');
                }
            }
        }

        function persistFlowEdgeInspector() {
            if (!renderer || !edgeConditionEl || !edgeMatchEl || !edgeLlmHintEl) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) return;
            var esel = cy.$('edge:selected');
            if (esel.length !== 1) return;
            var e = esel[0];
            e.data('condition', edgeConditionEl.value);
            e.data('conditionMatch', edgeMatchEl.value || 'contains');
            e.data('llmRoutingHint', edgeLlmHintEl.value);
            if (String(e.data('mode') || '') === 'loopBack' && loopRegionIdEl && loopMaxEl && loopInvalidationEl) {
                var maxA = parseInt(loopMaxEl.value, 10);
                e.data(
                    'loopConfig',
                    JSON.stringify({
                        loopRegionId: loopRegionIdEl.value.trim() || 'loop-' + e.data('source'),
                        maxAttempts: !isNaN(maxA) && maxA > 0 ? maxA : 3,
                        storeInvalidation: loopInvalidationEl.value || 'fromTargetForward',
                        incrementAttempt: true
                    })
                );
            }
            updateEdgeRouteCaption(e);
            refreshLoopRegionOverlay();
            setFlowMsg('');
        }

        function flowDocHasLlmRouter(doc) {
            var nodes = (doc && doc.nodes) || [];
            return nodes.some(function (n) {
                if (!n || n.type !== 'Router' || !n.config) return false;
                return String(n.config.routerMode || '').toLowerCase() === 'llm';
            });
        }

        /** Toggles Router panel sections so LLM-only fields appear only in LLM mode. */
        function updateRouterModeDependentUi() {
            var det = document.getElementById('sc-flow-router-deterministic');
            var llm = document.getElementById('sc-flow-router-llm-options');
            if (!routerModeEl || !det || !llm) return;
            var isLlm = routerModeEl.value === 'llm';
            det.classList.toggle('hidden', isLlm);
            llm.classList.toggle('hidden', !isLlm);
            if (routerMaxEl && routerTargetPolicyEl) {
                var single = routerTargetPolicyEl.value === 'single_best';
                routerMaxEl.disabled = single;
                routerMaxEl.classList.toggle('opacity-50', single);
            }
            if (routerBranchExecWrap && routerTargetPolicyEl) {
                var allMatch = routerTargetPolicyEl.value === 'all_matching';
                routerBranchExecWrap.classList.toggle('hidden', !allMatch);
            }
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
            if (routerTargetPolicyEl) {
                routerTargetPolicyEl.value =
                    cfg.routerTargetPolicy === 'single_best' ? 'single_best' : 'all_matching';
            }
            if (routerBranchExecEl) {
                var be = String(cfg.routerBranchExecution || 'parallel').toLowerCase();
                routerBranchExecEl.value =
                    be === 'sequential' ? 'sequential' : be === 'auto' ? 'auto' : 'parallel';
            }
            routerMaxEl.value = cfg.maxTargets != null && cfg.maxTargets !== '' ? String(cfg.maxTargets) : '';
            routerMinConfEl.value = cfg.minConfidence != null && cfg.minConfidence !== '' ? String(cfg.minConfidence) : '';
            routerFallbackEl.value = cfg.fallbackPersonaId ? String(cfg.fallbackPersonaId) : '';
            if (routerLlmInstrEl) {
                routerLlmInstrEl.value = cfg.llmRoutingInstructions ? String(cfg.llmRoutingInstructions) : '';
            }
            updateRouterModeDependentUi();
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
                var main =
                    tn.id +
                    ' → ' +
                    (pid === '(no personaId)' ? pid : getFlowPersonaLabel(pid) + ' (' + pid + ')');
                var bits = [main];
                if (e.condition && String(e.condition).trim()) {
                    bits.push('cond: ' + String(e.condition).trim().slice(0, 48));
                }
                if (e.llmRoutingHint && String(e.llmRoutingHint).trim()) {
                    bits.push('hint: ' + String(e.llmRoutingHint).trim().slice(0, 48));
                }
                li.textContent = bits.join(' — ');
                routerCandUl.appendChild(li);
            });
            var detPrev = document.getElementById('sc-flow-router-det-edge-preview');
            if (detPrev) {
                detPrev.innerHTML = '';
                var edgesSeq = (doc.edges || []).filter(function (edge) {
                    return edge && edge.fromNodeId === rid && (!edge.mode || edge.mode === 'sequential');
                });
                edgesSeq.sort(function (a, b) {
                    return String(a.id || '').localeCompare(String(b.id || ''));
                });
                if (edgesSeq.length === 0) {
                    var li0 = document.createElement('li');
                    li0.textContent = '(no sequential edges yet — connect this router to the next node)';
                    detPrev.appendChild(li0);
                } else {
                    edgesSeq.forEach(function (edge) {
                        var tn = (doc.nodes || []).filter(function (n) {
                            return n && n.id === edge.toNodeId;
                        })[0];
                        var tlabel = tn ? tn.type + ' "' + tn.id + '"' : '"' + edge.toNodeId + '"';
                        var c = String(edge.condition || '').trim();
                        var cm = String(edge.conditionMatch || 'contains');
                        var line = '→ ' + tlabel + ': ';
                        line += c
                            ? '"' + c.slice(0, 44) + (c.length > 44 ? '...' : '') + '" [' + cm + ']'
                            : '(default branch)';
                        var liE = document.createElement('li');
                        liE.textContent = line;
                        detPrev.appendChild(liE);
                    });
                }
            }
            updateRouterModeDependentUi();
        }

        function hostToolCatalogRow(toolId) {
            var tid = String(toolId || '').toLowerCase();
            var row = null;
            (hostCatalogTools || []).forEach(function (t) {
                if (t && String(t.id || '').toLowerCase() === tid) row = t;
            });
            return row;
        }

        function renderFlowYamlToolsBaseline(personaId) {
            if (!flowYamlToolsEl) return;
            flowYamlToolsEl.innerHTML = '';
            var pid = normalizeType(personaId);
            if (!pid) {
                flowYamlToolsEl.innerHTML =
                    '<span class="text-[9px] text-gray-500 dark:text-gray-400">Pick a persona first.</span>';
                return;
            }
            if (flowEditAgentLink) {
                flowEditAgentLink.href =
                    '/Dashboard/ProjectMemory/Agents/Edit?id=' + encodeURIComponent(pid);
            }
            var dto = personaToolsById[normPersonaKey(pid)];
            if (window.AgctorPersonaToolsUi && dto) {
                window.AgctorPersonaToolsUi.renderYamlBaselinePills(flowYamlToolsEl, dto);
                return;
            }
            ensurePersonaTools(pid).then(function (loaded) {
                if (window.AgctorPersonaToolsUi && loaded) {
                    window.AgctorPersonaToolsUi.renderYamlBaselinePills(flowYamlToolsEl, loaded);
                } else {
                    flowYamlToolsEl.innerHTML =
                        '<span class="text-[9px] text-gray-500 dark:text-gray-400">Could not load persona tools.</span>';
                }
            }).catch(function () {
                flowYamlToolsEl.innerHTML =
                    '<span class="text-[9px] text-red-600 dark:text-red-400">Failed to load tools.</span>';
            });
        }

        /** PRD-014: LlmNode.config.toolIds — grouped host tools eligible for this persona. */
        function renderFlowLlmExtraTools(cfg, effectivePid) {
            if (!flowAgentToolsSection || !flowLlmToolsEl) return;
            flowLlmToolsEl.innerHTML = '';
            var pid = normalizeType(effectivePid);
            if (!pid) {
                flowAgentToolsSection.classList.add('hidden');
                return;
            }
            flowAgentToolsSection.classList.remove('hidden');
            renderFlowYamlToolsBaseline(pid);

            var selected = sanitizeLlmNodeToolIdsForPersona(pid, cfg.toolIds);
            var dto = personaToolsById[normPersonaKey(pid)];
            if (window.AgctorPersonaToolsUi && dto) {
                window.AgctorPersonaToolsUi.renderFlowStepToolToggles(flowLlmToolsEl, dto, selected, esc);
                return;
            }
            ensurePersonaTools(pid).then(function (loaded) {
                if (window.AgctorPersonaToolsUi && loaded) {
                    window.AgctorPersonaToolsUi.renderFlowStepToolToggles(flowLlmToolsEl, loaded, selected, esc);
                } else {
                    flowLlmToolsEl.innerHTML =
                        '<p class="text-[9px] text-gray-500 dark:text-gray-400">Could not load persona tools.</p>';
                }
            }).catch(function () {
                flowLlmToolsEl.innerHTML =
                    '<p class="text-[9px] text-red-600 dark:text-red-400">Failed to load tools.</p>';
            });
        }

        function refreshFlowPersonaInspector() {
            if (!renderer || !personaPanel || !personaSelect || !personaRosterHint || !personaInvalidHint) return;
            var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
            if (!cy) {
                personaPanel.classList.add('hidden');
                if (pqContextWrap) pqContextWrap.classList.add('hidden');
                if (flowAgentToolsSection) flowAgentToolsSection.classList.add('hidden');
                if (flowYamlToolsEl) flowYamlToolsEl.innerHTML = '';
                if (personaCapEl) {
                    personaCapEl.innerHTML = '';
                    personaCapEl.classList.add('hidden');
                }
                return;
            }
            var sel = cy.$('node:selected');
            if (sel.length !== 1 || sel.data('agctorType') !== 'LlmNode') {
                personaPanel.classList.add('hidden');
                if (pqContextWrap) pqContextWrap.classList.add('hidden');
                if (flowAgentToolsSection) flowAgentToolsSection.classList.add('hidden');
                if (flowYamlToolsEl) flowYamlToolsEl.innerHTML = '';
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
            if (effectivePid) {
                var expectedLabel = getLlmNodeCanvasLabel(effectivePid);
                if (sel.data('label') !== expectedLabel) {
                    sel.data('label', expectedLabel);
                }
            }
            if (pqContextWrap && pqContextStrategyEl) {
                var showPq = effectivePid.toLowerCase() === 'person-query';
                pqContextWrap.classList.toggle('hidden', !showPq);
                if (showPq) {
                    var strat = (cfg.contextStrategy && String(cfg.contextStrategy).trim()) || 'markdown_all';
                    pqContextStrategyEl.value = ['markdown_all', 'markdown_focus', 'rag', 'graph_rag'].indexOf(strat) >= 0
                        ? strat
                        : 'markdown_all';
                }
            }
            renderFlowLlmExtraTools(cfg, effectivePid);
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
            var pid = cfg.personaId;
            if (pid && pid.toLowerCase() === 'person-query' && pqContextStrategyEl) {
                cfg.contextStrategy = pqContextStrategyEl.value || 'markdown_all';
            } else {
                delete cfg.contextStrategy;
            }
            var nextToolIds = [];
            if (flowLlmToolsEl) {
                flowLlmToolsEl.querySelectorAll('input[type="checkbox"][data-flow-tool-id]').forEach(function (cb) {
                    if (cb.checked) nextToolIds.push(cb.getAttribute('data-flow-tool-id') || '');
                });
            }
            nextToolIds = sanitizeLlmNodeToolIdsForPersona(pid, nextToolIds);
            if (nextToolIds.length) cfg.toolIds = nextToolIds;
            else delete cfg.toolIds;
            n.data('agctorConfig', JSON.stringify(cfg));
            n.data('label', getLlmNodeCanvasLabel(pid));
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
                if (routerTargetPolicyEl) {
                    cfg.routerTargetPolicy = routerTargetPolicyEl.value === 'single_best' ? 'single_best' : 'all_matching';
                }
                if (routerTargetPolicyEl && routerTargetPolicyEl.value === 'single_best') {
                    delete cfg.routerBranchExecution;
                } else if (routerBranchExecEl) {
                    var bev = routerBranchExecEl.value;
                    cfg.routerBranchExecution =
                        bev === 'sequential' ? 'sequential' : bev === 'auto' ? 'auto' : 'parallel';
                }
                var mx = routerMaxEl.value.trim();
                if (routerTargetPolicyEl && routerTargetPolicyEl.value === 'single_best') {
                    delete cfg.maxTargets;
                } else if (mx) {
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
                if (routerLlmInstrEl) {
                    var lix = routerLlmInstrEl.value.trim();
                    if (lix) cfg.llmRoutingInstructions = lix;
                    else delete cfg.llmRoutingInstructions;
                }
            } else {
                delete cfg.routerMode;
                delete cfg.routerTargetPolicy;
                delete cfg.routerBranchExecution;
                delete cfg.maxTargets;
                delete cfg.minConfidence;
                delete cfg.fallbackPersonaId;
                if (routerLlmInstrEl) delete cfg.llmRoutingInstructions;
            }
            n.data('agctorConfig', JSON.stringify(cfg));
            setFlowMsg('');
            refreshFlowRouterInspector();
            refreshFlowEdgeInspector();
        }

        function openModal() {
            var s = currentScenario();
            if (!s) return;
            var existingFlow = getScenarioFlow(s);
            draftBase = existingFlow ? JSON.parse(JSON.stringify(existingFlow)) : window.AgctorScenarioFlow.emptyFlow(s.id);
            resetFlowSimState();
            populateGateFactOptions();
            populateGateOperatorOptions();
            populateAwaitEventOptions();
            renderer = window.AgctorScenarioFlow.createGraphRenderer();
            cyHost.innerHTML = '';
            modal.classList.remove('hidden');
            api('/api/Config')
                .then(function (cfg) {
                    hostCatalogTools = Array.isArray(cfg && cfg.tools) ? cfg.tools.slice() : [];
                })
                .catch(function () { /* keep prior hostCatalogTools */ })
                .finally(function () {
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
                            updateV2StudioUi();
                            loadFlowModalAgentLabels().finally(function () {
                                refreshFlowInspectors();
                            });
                            if (typeof renderer.fitViewport === 'function') renderer.fitViewport();
                            setFlowMsg('<span class="text-gray-500">Validate or save when done. Select an <strong>edge</strong> to edit routing rules.</span>');
                        });
                    });
                });
        }

        if (routerModeEl) routerModeEl.addEventListener('change', function () {
            updateRouterModeDependentUi();
            persistFlowRouterInspector();
        });
        if (routerTargetPolicyEl) {
            routerTargetPolicyEl.addEventListener('change', function () {
                updateRouterModeDependentUi();
                persistFlowRouterInspector();
            });
        }
        if (routerMaxEl) routerMaxEl.addEventListener('change', persistFlowRouterInspector);
        if (routerMinConfEl) routerMinConfEl.addEventListener('change', persistFlowRouterInspector);
        if (routerFallbackEl) routerFallbackEl.addEventListener('change', persistFlowRouterInspector);
        if (routerLlmInstrEl) routerLlmInstrEl.addEventListener('change', persistFlowRouterInspector);
        if (routerLlmInstrEl) routerLlmInstrEl.addEventListener('blur', persistFlowRouterInspector);
        if (personaSelect) personaSelect.addEventListener('change', persistFlowPersonaInspector);
        if (pqContextStrategyEl) pqContextStrategyEl.addEventListener('change', persistFlowPersonaInspector);
        if (flowLlmToolsEl) flowLlmToolsEl.addEventListener('change', persistFlowPersonaInspector);
        if (edgeConditionEl) edgeConditionEl.addEventListener('change', persistFlowEdgeInspector);
        if (edgeConditionEl) edgeConditionEl.addEventListener('blur', persistFlowEdgeInspector);
        if (edgeMatchEl) edgeMatchEl.addEventListener('change', persistFlowEdgeInspector);
        if (edgeLlmHintEl) edgeLlmHintEl.addEventListener('change', persistFlowEdgeInspector);
        if (edgeLlmHintEl) edgeLlmHintEl.addEventListener('blur', persistFlowEdgeInspector);
        if (loopRegionIdEl) loopRegionIdEl.addEventListener('change', persistFlowEdgeInspector);
        if (loopMaxEl) loopMaxEl.addEventListener('change', persistFlowEdgeInspector);
        if (loopInvalidationEl) loopInvalidationEl.addEventListener('change', persistFlowEdgeInspector);
        if (gateFactEl) gateFactEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (gateOpEl) gateOpEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (gateTrueEdgeEl) gateTrueEdgeEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (gateFalseEdgeEl) gateFalseEdgeEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (waitPromptEl) waitPromptEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (waitPromptEl) waitPromptEl.addEventListener('blur', persistFlowV2NodeInspectors);
        if (waitAttachmentsEl) waitAttachmentsEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (waitPolicyEl) waitPolicyEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (awaitEventEl) awaitEventEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (awaitTimeoutEl) awaitTimeoutEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (notifyTargetEl) notifyTargetEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (notifySignalEl) notifySignalEl.addEventListener('change', persistFlowV2NodeInspectors);
        if (showLoopRegionsEl) {
            showLoopRegionsEl.addEventListener('change', refreshLoopRegionOverlay);
        }

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
                var nodeLabel = add;
                if (add === 'LlmNode') {
                    var s2 = currentScenario();
                    var ids = (s2 && s2.personaAgentIds) || [];
                    if (!ids.length) {
                        setFlowMsg('<span class="text-amber-700 dark:text-amber-300">No YAML personas on this scenario — add persona chips on the form, then assign each LlmNode node.</span>');
                        cfg.personaId = '';
                    } else {
                        cfg.personaId = normalizeType(ids[0]);
                    }
                    // Server-side playground person-query loads markdown; default matches designer dropdown.
                    cfg.contextStrategy = 'markdown_all';
                    var pid0 = cfg.personaId ? normalizeType(cfg.personaId).toLowerCase() : '';
                    if (pid0 === 'person-query') cfg.toolIds = ['person-memory-context'];
                    else if (pid0 === 'memory-curator') cfg.toolIds = ['apply-memory-intents'];
                    nodeLabel = getLlmNodeCanvasLabel(cfg.personaId);
                } else if (window.AgctorScenarioFlow && typeof window.AgctorScenarioFlow.defaultConfigForNodeType === 'function') {
                    var v2Types = window.AgctorScenarioFlow.nodeTypesV2 || [];
                    if (v2Types.indexOf(add) >= 0) {
                        cfg = window.AgctorScenarioFlow.defaultConfigForNodeType(add);
                        if (typeof window.AgctorScenarioFlow.defaultLabelForNodeType === 'function') {
                            nodeLabel = window.AgctorScenarioFlow.defaultLabelForNodeType(add);
                        }
                    }
                }
                renderer.addNode(add, nodeLabel, cfg);
                refreshFlowInspectors();
                updateV2StudioUi();
            }
        });

        btnConnect.addEventListener('click', function () {
            if (!renderer) return;
            var ok = renderer.connectSelected('sequential');
            setFlowMsg(ok ? '<span class="text-emerald-600">Connected selected nodes.</span>' : '<span class="text-amber-700">Select exactly two nodes (box-select).</span>');
            refreshFlowInspectors();
        });

        if (btnConnectLoop) {
            btnConnectLoop.addEventListener('click', function () {
                if (!renderer) return;
                var ok = renderer.connectSelected('loopBack');
                setFlowMsg(ok
                    ? '<span class="text-emerald-600">Loop-back edge created — select it to configure region and max attempts.</span>'
                    : '<span class="text-amber-700">Select exactly two nodes (typically Ask user → earlier step).</span>');
                refreshFlowInspectors();
            });
        }

        if (edgeConvertLoopBtn) {
            edgeConvertLoopBtn.addEventListener('click', function () {
                if (!renderer) return;
                var cy = typeof renderer.getCy === 'function' ? renderer.getCy() : null;
                if (!cy) return;
                var esel = cy.$('edge:selected');
                if (esel.length !== 1) return;
                var e = esel[0];
                if (String(e.data('mode') || '') === 'loopBack') return;
                var fromId = e.data('source');
                var lc =
                    window.AgctorScenarioFlow && typeof window.AgctorScenarioFlow.defaultLoopConfig === 'function'
                        ? window.AgctorScenarioFlow.defaultLoopConfig(fromId)
                        : { loopRegionId: 'loop-' + fromId, maxAttempts: 3, storeInvalidation: 'fromTargetForward', incrementAttempt: true };
                e.data('mode', 'loopBack');
                e.data('loopConfig', JSON.stringify(lc));
                updateEdgeRouteCaption(e);
                refreshFlowEdgeInspector();
                refreshLoopRegionOverlay();
                updateV2StudioUi();
                setFlowMsg('<span class="text-emerald-600">Edge converted to loop back.</span>');
            });
        }

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
            if (sim.ok) {
                setFlowMsg(buildTraverseOrderHtml(sim.steps, llmNote));
                linkTraverseHover(sim.steps);
            } else {
                setFlowMsg('<span class="text-red-600">' + esc(sim.errors.join('; ')) + '</span>');
            }
        });

        if (btnSimulateTurns && simulateTurnsBar) {
            btnSimulateTurns.addEventListener('click', function () {
                simulateTurnsBar.classList.toggle('hidden');
                if (!simulateTurnsBar.classList.contains('hidden') && turnMessageEl) {
                    turnMessageEl.focus();
                }
            });
        }
        if (btnTurnRun) {
            btnTurnRun.addEventListener('click', function () {
                runSimulateTurn({
                    message: turnMessageEl ? turnMessageEl.value : '',
                    attachments: turnAttachmentsEl && turnAttachmentsEl.checked ? ['img1'] : []
                });
            });
        }
        if (btnEventRun) {
            btnEventRun.addEventListener('click', function () {
                var ev = flowSimState && flowSimState.awaitingEvent
                    ? flowSimState.awaitingEvent
                    : (awaitEventEl ? awaitEventEl.value : 'visual.extract.completed');
                runSimulateTurn({ eventType: ev });
            });
        }
        if (btnTurnReset) {
            btnTurnReset.addEventListener('click', function () {
                resetFlowSimState();
                setFlowMsg('<span class="text-gray-500">Simulate reset — run a turn to start from ChatInput.</span>');
            });
        }

        btnSaveFlow.addEventListener('click', function () {
            var s = currentScenario();
            if (!s) {
                setFlowMsg('<span class="text-amber-700">Nothing to save (open the flow editor first).</span>');
                return;
            }
            var doc = readFlowDocFromRenderer();
            if (!doc) {
                setFlowMsg('<span class="text-red-600">Graph is not ready yet — wait a second after opening, then try again.</span>');
                return;
            }
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

        if (btnCopyFlow) {
            btnCopyFlow.addEventListener('click', openCopyFlowModal);
        }

        if (copyModal) {
            copyModal.querySelectorAll('[data-sc-flow-copy-cancel], [data-sc-flow-copy-backdrop]').forEach(function (el) {
                el.addEventListener('click', function () {
                    if (copyStepConfirm && !copyStepConfirm.classList.contains('hidden')) {
                        resetCopyFlowModal();
                        return;
                    }
                    closeCopyFlowModal();
                });
            });
            copyModal.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && !copyModal.classList.contains('hidden')) closeCopyFlowModal();
            });
        }

        if (copyApproveBtn) {
            copyApproveBtn.addEventListener('click', function () {
                if (copyErrorEl) {
                    copyErrorEl.classList.add('hidden');
                    copyErrorEl.textContent = '';
                }
                var targetId = copyTargetEl ? String(copyTargetEl.value || '').trim() : '';
                if (!targetId) {
                    if (copyErrorEl) {
                        copyErrorEl.textContent = 'Choose a target scenario.';
                        copyErrorEl.classList.remove('hidden');
                    }
                    return;
                }
                var source = currentScenario();
                if (source && targetId === source.id) {
                    if (copyErrorEl) {
                        copyErrorEl.textContent = 'Choose a different scenario than the one you are editing.';
                        copyErrorEl.classList.remove('hidden');
                    }
                    return;
                }
                if (targetScenarioHasSavedFlow(targetId)) {
                    showCopyOverrideStep(targetId);
                    return;
                }
                performCopyFlowToTarget(targetId).catch(function (e) {
                    var m = (e && e.message) ? String(e.message) : 'Copy failed';
                    if (copyErrorEl) {
                        copyErrorEl.textContent = m;
                        copyErrorEl.classList.remove('hidden');
                    }
                    setFlowMsg('<span class="text-red-600">' + esc(m) + '</span>');
                });
            });
        }

        if (copyOverrideBtn) {
            copyOverrideBtn.addEventListener('click', function () {
                if (!copyPendingTargetId) {
                    resetCopyFlowModal();
                    return;
                }
                performCopyFlowToTarget(copyPendingTargetId).catch(function (e) {
                    var m = (e && e.message) ? String(e.message) : 'Copy failed';
                    if (copyStepSelect) copyStepSelect.classList.remove('hidden');
                    if (copyStepConfirm) copyStepConfirm.classList.add('hidden');
                    if (copyApproveBtn) copyApproveBtn.classList.remove('hidden');
                    if (copyOverrideBtn) copyOverrideBtn.classList.add('hidden');
                    if (copyErrorEl) {
                        copyErrorEl.textContent = m;
                        copyErrorEl.classList.remove('hidden');
                    }
                    setFlowMsg('<span class="text-red-600">' + esc(m) + '</span>');
                });
            });
        }
    })();

    loadAll();
})();

