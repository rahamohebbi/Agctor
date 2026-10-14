/**
 * Agents workspace: running instances, saved definitions, and type availability.
 * Keeps view state and editor contents stable across background refreshes.
 */
(function () {
    const el = document.getElementById('agents-content');
    if (!el) return;

    const drawerHost = document.getElementById('agents-pm-drawer-host');
    const drawerPanel = document.getElementById('agents-pm-drawer-panel');

    let configData = null;
    let agentsData = [];
    let definitionsData = [];
    let scenariosData = [];
    /** GET /api/agents/definitions/tool-usage — host tools per YAML / C# agent (dynamic). */
    let agentToolsInsight = null;
    let currentScenario = null;
    let lastLoadedAt = null;
    const backendErrors = new Map();
    let refreshTimer = null;
    let lastRenderSignature = '';
    let activeTab = 'running';
    let searchText = '';
    let selectedScenario = '';
    let scenarioExpanded = false;
    let mutationBusy = false;
    let refreshBusy = false;
    let drawerReturnFocus = null;
    let drawerReturnFocusId = null;
    let originalPmSpec = {};
    let feedback = { message: '', error: false };
    let scenarioApplying = false;

    /** PRD-013 Phase 2: create / edit project-memory YAML from unified Agents page */
    let pmDrawerMode = 'create'; // create | edit

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function setBackendError(key, message) {
        if (!message) backendErrors.delete(key);
        else backendErrors.set(key, message);
    }

    function clearBackendError(key) {
        backendErrors.delete(key);
    }

    function renderBackendErrors() {
        const errors = Array.from(backendErrors.entries());
        if (errors.length === 0) return '';
        return (
            '<div class="p-4 mb-4 rounded-lg bg-red-50 border border-red-200 dark:bg-red-900/20 dark:border-red-800" role="alert">' +
            '<p class="text-sm font-medium text-red-800 dark:text-red-200">Backend issues</p>' +
            '<ul class="mt-2 space-y-1 text-sm text-red-700 dark:text-red-300">' +
            errors.map(([key, msg]) => '<li><strong>' + esc(key) + ':</strong> ' + esc(msg) + '</li>').join('') +
            '</ul></div>'
        );
    }

    function stableErrorsSnapshot() {
        return Array.from(backendErrors.entries())
            .sort(function (a, b) {
                return String(a[0]).localeCompare(String(b[0]));
            })
            .map(function (kv) {
                return [kv[0], kv[1]];
            });
    }

    function buildRenderSignature(config, agents, current, definitions, scenarios, agentTools) {
        return JSON.stringify({
            config: config || {},
            agents: Array.isArray(agents) ? agents.map(a => ({ id: a.id, type: a.type, status: a.status })) : [],
            current: current || null,
            definitions: Array.isArray(definitions) ? definitions : [],
            scenarios: Array.isArray(scenarios) ? scenarios : [],
            agentTools: agentTools && Array.isArray(agentTools.agents) ? agentTools.agents : [],
            errors: stableErrorsSnapshot()
        });
    }

    function linesToArr(t) {
        if (!t) return [];
        return t
            .split('\n')
            .map(function (l) {
                return l.trim();
            })
            .filter(Boolean);
    }

    function arrToLines(a) {
        return Array.isArray(a) ? a.join('\n') : '';
    }

    function splitComma(s) {
        if (!s) return [];
        return s
            .split(',')
            .map(function (x) {
                return x.trim();
            })
            .filter(Boolean);
    }

    function joinComma(a) {
        return Array.isArray(a) ? a.join(', ') : '';
    }

    function defaultPmSpec() {
        return {
            id: '',
            name: '',
            role: '',
            description: '',
            projectTypes: [],
            instructions: [],
            input: { type: '' },
            output: { type: '' },
            tools: { allow: [], deny: [] },
            memoryAccess: { read: [], write: [] },
            guardrails: []
        };
    }

    function normalizePmSpec(raw) {
        const s = raw && typeof raw === 'object' ? raw : {};
        const d = defaultPmSpec();
        d.id = typeof s.id === 'string' ? s.id : d.id;
        d.name = typeof s.name === 'string' ? s.name : d.name;
        d.role = typeof s.role === 'string' ? s.role : d.role;
        d.description = typeof s.description === 'string' ? s.description : d.description;
        d.projectTypes = Array.isArray(s.projectTypes) ? s.projectTypes.slice() : d.projectTypes;
        d.instructions = Array.isArray(s.instructions) ? s.instructions.slice() : d.instructions;
        d.input = s.input && typeof s.input === 'object' ? { type: s.input.type || '' } : d.input;
        d.output = s.output && typeof s.output === 'object' ? { type: s.output.type || '' } : d.output;
        d.tools =
            s.tools && typeof s.tools === 'object'
                ? {
                      allow: Array.isArray(s.tools.allow) ? s.tools.allow.slice() : [],
                      deny: Array.isArray(s.tools.deny) ? s.tools.deny.slice() : []
                  }
                : d.tools;
        d.memoryAccess =
            s.memoryAccess && typeof s.memoryAccess === 'object'
                ? {
                      read: Array.isArray(s.memoryAccess.read) ? s.memoryAccess.read.slice() : [],
                      write: Array.isArray(s.memoryAccess.write) ? s.memoryAccess.write.slice() : []
                  }
                : d.memoryAccess;
        d.guardrails = Array.isArray(s.guardrails) ? s.guardrails.slice() : d.guardrails;
        return d;
    }

    function closePmDrawer() {
        if (!drawerHost) return;
        drawerHost.classList.add('hidden');
        drawerHost.setAttribute('aria-hidden', 'true');
        if (drawerPanel) drawerPanel.innerHTML = '';
        if (drawerReturnFocus?.isConnected) drawerReturnFocus.focus();
        else (document.getElementById(drawerReturnFocusId) || document.getElementById('agents-tab-' + activeTab))?.focus();
    }

    function readPmFormFromPanel() {
        const spec = normalizePmSpec({});
        spec.id = /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-id')).value.trim();
        spec.name = /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-name')).value.trim();
        spec.role = /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-role')).value.trim();
        spec.description = /** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-desc')).value;
        spec.projectTypes = splitComma(/** @type {HTMLInputElement} */ (document.getElementById('agents-pm-pt')).value);
        spec.instructions = linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-ins')).value);
        spec.input = { type: /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-in')).value.trim() };
        spec.output = { type: /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-out')).value.trim() };
        spec.tools = {
            allow: linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-tallow')).value),
            deny: linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-tdeny')).value)
        };
        spec.memoryAccess = {
            read: linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-mr')).value),
            write: linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-mw')).value)
        };
        spec.guardrails = linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-g')).value);
        const rel = /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-relpath')).value.trim();
        const merged = { ...originalPmSpec, ...spec };
        for (const key of ['input', 'output', 'tools', 'memoryAccess']) merged[key] = { ...(originalPmSpec[key] || {}), ...spec[key] };
        return { spec: merged, relativePath: rel || null };
    }

    function renderPmDrawerForm(spec, relativePath, title, readOnly) {
        if (!drawerPanel) return;
        const idReadonly = readOnly || pmDrawerMode === 'edit';
        const field = (label, id, rows, hint = '', locked = readOnly) => `<div><label for="${id}" class="block text-sm font-medium text-gray-800 dark:text-gray-200 mb-1">${label}</label>
            ${hint ? `<p class="text-xs text-gray-500 dark:text-gray-400 mb-2">${hint}</p>` : ''}
            ${rows ? `<textarea id="${id}" rows="${rows}" ${locked ? 'readonly' : ''}` : `<input id="${id}" type="text" ${locked ? 'readonly' : ''}`}
            class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-sm dark:bg-gray-800 dark:border-gray-600 dark:text-white">${rows ? '</textarea>' : ''}</div>`;
        drawerPanel.innerHTML = `<div class="p-6 space-y-6">
            <div class="flex items-start justify-between gap-4"><h2 class="text-lg font-semibold text-gray-900 dark:text-white">${esc(title)}</h2><button type="button" data-agents-drawer-close class="text-sm text-gray-500 dark:text-gray-400">Close</button></div>
            <p class="text-sm text-gray-500 dark:text-gray-400">Save reusable instructions for a workflow. This creates a definition, not a running instance.</p>
            <div class="space-y-4">
                ${field('Name', 'agents-pm-name', 0)}
                ${field('Definition ID', 'agents-pm-id', 0, 'The identifier used by workflows. It cannot change after creation.', idReadonly)}
                ${field('Role', 'agents-pm-role', 0, 'For example: coaching, extraction, or query.')}
                ${field('Description', 'agents-pm-desc', 2, 'A short explanation of what this agent does.')}
                ${field('Instructions', 'agents-pm-ins', 5, 'One instruction per line.')}
                ${field('Guardrails', 'agents-pm-g', 2, 'Optional boundaries and constraints, one per line.')}
            </div>
            <details class="border-t border-gray-200 dark:border-gray-700 pt-4"><summary class="cursor-pointer text-sm font-semibold">Tools and memory permissions</summary><div class="space-y-4 mt-4">
                ${field('Allowed tools', 'agents-pm-tallow', 3, 'Tool names, one per line.')}
                ${field('Denied tools', 'agents-pm-tdeny', 2)}
                ${field('Memory read paths', 'agents-pm-mr', 3, 'One path or pattern per line.')}
                ${field('Memory write paths', 'agents-pm-mw', 2)}
            </div></details>
            <details class="border-t border-gray-200 dark:border-gray-700 pt-4"><summary class="cursor-pointer text-sm font-semibold">Advanced configuration</summary><div class="space-y-4 mt-4">
                ${field('Project types', 'agents-pm-pt', 0, 'Comma-separated project types, such as people.')}
                ${field('Input type', 'agents-pm-in', 0)}
                ${field('Output type', 'agents-pm-out', 0)}
                ${field('Relative file path', 'agents-pm-relpath', 0, 'Optional. The file is saved under the configured project root.')}
                <a href="/Dashboard/ProjectMemory/Agents" class="text-sm text-blue-600 dark:text-blue-400">Open the full agent editor ↗</a>
            </div></details>
            ${readOnly ? '' : '<div class="sticky bottom-0 bg-white dark:bg-gray-900 border-t border-gray-200 dark:border-gray-700 pt-4 pb-2"><button type="button" data-pm-save class="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700">Save definition</button><p id="agents-pm-msg" role="status" aria-live="polite" class="mt-2 text-sm text-gray-600 dark:text-gray-400"></p></div>'}
            </div>`;

        /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-id')).value = spec.id;
        /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-relpath')).value = relativePath || '';
        /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-name')).value = spec.name;
        /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-role')).value = spec.role;
        /** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-desc')).value = spec.description;
        /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-pt')).value = joinComma(spec.projectTypes);
        /** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-ins')).value = arrToLines(spec.instructions);
        /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-in')).value = spec.input.type;
        /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-out')).value = spec.output.type;
        /** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-tallow')).value = arrToLines(spec.tools.allow);
        /** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-tdeny')).value = arrToLines(spec.tools.deny);
        /** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-mr')).value = arrToLines(spec.memoryAccess.read);
        /** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-mw')).value = arrToLines(spec.memoryAccess.write);
        /** @type {HTMLTextAreaElement} */ (document.getElementById('agents-pm-g')).value = arrToLines(spec.guardrails);
    }

    function openPmDrawer() {
        if (!drawerHost) return;
        drawerReturnFocus = document.activeElement;
        drawerReturnFocusId = document.activeElement?.id;
        drawerHost.classList.remove('hidden');
        drawerHost.setAttribute('aria-hidden', 'false');
        drawerPanel.querySelectorAll('label').forEach(label => {
            const field = label.parentElement.querySelector('input, textarea');
            if (field?.id) label.htmlFor = field.id;
        });
        (drawerPanel.querySelector('input:not([readonly]), button') || drawerPanel).focus();
    }

    async function openNewPmAgent() {
        pmDrawerMode = 'create';
        originalPmSpec = {};
        renderPmDrawerForm(normalizePmSpec(defaultPmSpec()), '', 'New agent definition', false);
        openPmDrawer();
    }

    async function openEditPmAgent(id) {
        const res = await fetch('/api/agents/definitions/' + encodeURIComponent(id));
        const data = await res.json().catch(function () {
            return null;
        });
        if (!res.ok) {
            alert((data && (data.error || data.message)) || 'Failed to load definition (' + res.status + ').');
            return;
        }
        if (data.kind !== 'project-memory-yaml' || !data.detail || !data.detail.spec) {
            alert('Unexpected response shape.');
            return;
        }
        pmDrawerMode = 'edit';
        originalPmSpec = data.detail.spec;
        renderPmDrawerForm(normalizePmSpec(data.detail.spec), data.detail.relativePath || '', 'Edit ' + (data.detail.spec.name || id), false);
        openPmDrawer();
    }

    async function savePmAgent() {
        const msg = document.getElementById('agents-pm-msg');
        const payload = readPmFormFromPanel();
        if (!payload.spec.id) {
            if (msg) msg.textContent = 'Id is required.';
            return;
        }
        if (msg) msg.textContent = 'Saving…';
        const isCreate = pmDrawerMode === 'create';
        const url = isCreate
            ? '/api/agents/definitions/project-memory'
            : '/api/agents/definitions/project-memory/' + encodeURIComponent(payload.spec.id);
        const method = isCreate ? 'POST' : 'PUT';
        try {
            const res = await fetch(url, {
                method: method,
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ spec: payload.spec, relativePath: payload.relativePath })
            });
            const body = await res.json().catch(function () {
                return {};
            });
            if (!res.ok) {
                const err = body.error || body.message || 'Save failed (' + res.status + ').';
                if (msg) msg.textContent = err;
                return;
            }
            if (msg) msg.textContent = 'Saved.';
            pmDrawerMode = 'edit';
            /** @type {HTMLInputElement} */ (document.getElementById('agents-pm-id')).readOnly = true;
            await refreshRuntimeData(false);
        } catch (e) {
            if (msg) msg.textContent = 'Error: ' + (e.message || e);
        }
    }

    async function deletePmAgent(id) {
        if (!confirm('Delete YAML agent "' + id + '" from disk?')) return;
        try {
            const res = await fetch('/api/agents/definitions/project-memory/' + encodeURIComponent(id), { method: 'DELETE' });
            if (!res.ok) {
                const body = await res.json().catch(function () {
                    return {};
                });
                alert(body.error || body.message || 'Delete failed (' + res.status + ').');
                return;
            }
            closePmDrawer();
            await refreshRuntimeData(false);
        } catch (e) {
            alert('Error: ' + (e.message || e));
        }
    }

    function displayType(name) {
        return String(name || 'Unknown').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2');
    }

    function matchesSearch(...values) {
        return values.join(' ').toLowerCase().includes(searchText.trim().toLowerCase());
    }

    function toolDetails(id, kind) {
        const row = agentToolsInsight?.agents?.find(a => a.agentId === id && a.kind === kind);
        const tools = row?.tools || [];
        const unmatched = row?.unmappedYamlAllowTokens || [];
        return `<details class="ag-details" data-detail-key="${esc(kind + ':' + id)}"><summary>Tool access <span class="ag-count">${tools.length}</span></summary>
            <p class="ag-muted">Configured associations, not current tool activity.</p>
            ${tools.length ? '<ul class="ag-tool-list">' + tools.map(t => `<li><strong>${esc(t.displayName || t.clrTypeName)}</strong><span>${esc(t.description || '')}</span></li>`).join('') + '</ul>' : '<p class="ag-muted">No host tool associations found.</p>'}
            ${unmatched.length ? `<p class="ag-muted">Unresolved tool names: ${unmatched.map(esc).join(', ')}</p>` : ''}
            </details>`;
    }

    function emptyState(title, description) {
        return `<div class="ag-empty"><span class="ag-empty-icon" aria-hidden="true">◎</span><h3>${esc(title)}</h3><p>${esc(description)}</p>
            ${searchText ? '<button class="ag-button" data-clear-search>Clear search</button>' : ''}</div>`;
    }

    function renderRunning(list) {
        const visible = list.filter(a => matchesSearch(a.id, a.type, displayType(a.type)));
        return `<div class="ag-section-heading"><div><h2>Running agents</h2><p>Instances registered in this host session. Open an agent to inspect its details.</p></div><span class="ag-badge ag-badge-green">${list.length} registered</span></div>
            ${visible.length ? '<div class="ag-instance-list">' + visible.map(a => `<article class="ag-instance">
                <span class="ag-avatar" aria-hidden="true">${esc((a.type || 'A').slice(0, 1))}</span>
                <div class="ag-instance-body"><h3><a href="/Dashboard/AgentDetail/${encodeURIComponent(a.id)}">${esc(a.id)}</a></h3><p>${esc(displayType(a.type))}</p></div>
                <a class="ag-button" href="/Dashboard/AgentDetail/${encodeURIComponent(a.id)}" aria-label="Inspect ${esc(a.id)}">Inspect <span aria-hidden="true">↗</span></a>
                </article>`).join('') + '</div>' : emptyState(searchText ? 'No matching agents' : 'No agents are running yet', searchText ? 'Try an agent name or type.' : 'Apply a scenario above to create agents for a workflow. Enabling an agent type alone does not start it.')}
            <p class="ag-footnote">This is a registry view, not a live activity or health indicator.</p>`;
    }

    function renderLibrary(defs) {
        const visible = defs.filter(d => matchesSearch(d.displayName, d.id, d.description, d.metadata?.role, ...(d.metadata?.projectTypes || [])));
        return `<div class="ag-section-heading"><div><h2>Saved agent definitions</h2><p>Reusable instructions for project-memory workflows. Saving a definition does not start an agent.</p></div>
            <button class="ag-button ag-primary" id="agents-new-definition" data-yaml-new>+ New definition</button></div>
            ${visible.length ? '<div class="ag-card-grid">' + visible.map(d => `<article class="ag-card">
                <div class="ag-card-heading"><h3>${esc(d.displayName || d.id)}</h3><span class="ag-badge">${esc(d.metadata?.role || 'Definition')}</span></div>
                <p class="ag-muted ag-id">${esc(d.id)}</p>
                <p class="ag-description">${esc(d.description || 'Reusable instructions for project-memory workflows.')}</p>
                <p class="ag-card-context">${d.metadata?.projectTypes?.length ? 'For ' + d.metadata.projectTypes.map(esc).join(', ') + ' projects' : 'Project-memory definition'}</p>
                ${toolDetails(d.id, 'project-memory-yaml')}
                <div class="ag-card-actions"><button class="ag-button" id="agents-edit-${esc(d.id)}" data-yaml-edit="${esc(d.id)}" aria-label="Edit ${esc(d.displayName || d.id)}">Edit definition</button>
                <details class="ag-more" data-detail-key="source:${esc(d.id)}"><summary>File details</summary><div><p class="ag-file">${esc(d.source)}</p><button class="ag-danger" data-yaml-del="${esc(d.id)}">Delete definition</button></div></details></div>
                </article>`).join('') + '</div>' : emptyState(searchText ? 'No matching definitions' : 'Your agent library is empty', searchText ? 'Search by name, role, or project type.' : 'Create a definition with instructions, tool access, and memory permissions for a workflow.')}
            <p class="ag-footnote">Need templates or the full editor? <a href="/Dashboard/ProjectMemory/Agents">Open advanced agent settings ↗</a></p>`;
    }

    function renderTypes(config, list) {
        const types = config.agentTypes || {};
        const byType = groupAgentsByType(list);
        const visible = Object.keys(types).sort().filter(t => matchesSearch(t, displayType(t), types[t]));
        return `<div class="ag-section-heading"><div><h2>Agent types</h2><p>Built-in C# implementations available to scenarios and workflows.</p></div></div>
            <div class="ag-notice"><strong>Available does not mean running.</strong> Enabling a type allows it to be created. Disabling it stops its existing instances. These settings are saved across host restarts.</div>
            ${visible.length ? '<div class="ag-types">' + visible.map(t => {
                const enabled = config.agentTypeEnablement?.[t] !== false;
                const instances = byType.get(t) || [];
                return `<article class="ag-type"><div class="ag-type-main"><div><h3>${esc(displayType(t))}</h3><p class="ag-muted ag-id">${esc(t)}</p></div>
                    <div class="ag-type-controls"><span class="ag-muted">${instances.length} registered</span>
                    <label class="ag-toggle"><input type="checkbox" role="switch" id="toggle-${esc(t)}" data-agent-type-toggle="${esc(t)}" aria-label="Allow ${esc(displayType(t))} to be created" ${enabled ? 'checked' : ''} ${mutationBusy ? 'disabled' : ''}><span class="ag-switch" aria-hidden="true"></span><span>${enabled ? 'Available' : 'Disabled'}</span></label></div></div>
                    <details class="ag-details" data-detail-key="type:${esc(t)}"><summary>Type details</summary><p class="ag-file">${esc(types[t])}</p>
                    ${instances.length ? '<p class="ag-muted">Registered instances</p><ul class="ag-tool-list">' + instances.map(a => `<li><a href="/Dashboard/AgentDetail/${encodeURIComponent(a.id)}">${esc(a.id)}</a></li>`).join('') + '</ul>' : '<p class="ag-muted">No instances have been created.</p>'}
                    ${toolDetails(t, 'csharp-agent-type')}</details></article>`;
            }).join('') + '</div>' : emptyState('No matching types', 'Try a type name such as Coder or Session.')}`;
    }

    function groupAgentsByType(agents) {
        const map = new Map();
        for (const a of agents) {
            const type = a.type || 'Unknown';
            if (!map.has(type)) map.set(type, []);
            map.get(type).push(a);
        }
        return map;
    }

    function render(config, agents, current, definitions, scenarios, agentTools) {
        const focusedId = el.contains(document.activeElement) ? document.activeElement.id : null;
        const selection = focusedId === 'agents-search' ? document.activeElement.selectionStart : null;
        const openDetails = new Set(Array.from(el.querySelectorAll('details[open][data-detail-key]'), d => d.dataset.detailKey));
        const list = Array.isArray(agents) ? agents : [];
        const defs = (definitions || []).filter(d => d.kind === 'project-memory-yaml');
        const types = Object.keys(config.agentTypes || {});
        if (!selectedScenario) selectedScenario = current?.scenarioName || config.dashboardScenarioName || scenarios[0]?.id || '';
        const scenarioLabel = scenarios.find(s => s.id === current?.scenarioName)?.displayName || current?.scenarioName;
        const tabs = [['running', 'Running', list.length], ['library', 'Agent library', defs.length], ['types', 'Agent types', types.length]];
        el.innerHTML = `${renderBackendErrors()}
            <div class="ag-session"><div class="ag-session-copy"><span class="ag-eyebrow">CURRENT HOST SESSION</span><h2>${current ? esc(scenarioLabel) : 'No scenario applied'}</h2>
            <p>${current ? 'A scenario has been applied to this host.' : 'Startup agents can run independently. Apply a scenario to set up a workflow.'}</p></div>
            <button class="ag-button" id="agents-scenario-toggle" data-scenario-toggle aria-expanded="${scenarioExpanded}" aria-controls="agents-scenario-panel">${scenarioExpanded ? 'Close scenario setup' : current ? 'Change scenario' : 'Set up a scenario'} <span aria-hidden="true">${scenarioExpanded ? '−' : '+'}</span></button></div>
            <div class="ag-scenario-panel" id="agents-scenario-panel" ${scenarioExpanded ? '' : 'hidden'}>
                <label for="agents-scenario-select">Choose a scenario</label><div class="ag-scenario-controls"><select id="agents-scenario-select">${scenarios.map(s => `<option value="${esc(s.id)}" ${s.id === selectedScenario ? 'selected' : ''}>${esc(s.displayName || s.id)}</option>`).join('')}</select>
                <button class="ag-button ag-primary" data-apply-scenario ${mutationBusy || !scenarios.length ? 'disabled' : ''}>${scenarioApplying ? 'Applying…' : 'Apply scenario'}</button><a href="/Dashboard/Scenarios">Browse scenarios ↗</a></div>
                <p>Creates or configures agents for this host session. Applying a scenario may reset its existing agents. Your startup default stays unchanged.</p></div>
            <div id="scenario-message" class="ag-feedback" role="status" aria-live="polite"></div>
            <div class="ag-tabs" role="tablist" aria-label="Agent views">${tabs.map(([id, label, count]) => `<button id="agents-tab-${id}" role="tab" data-agent-tab="${id}" aria-selected="${activeTab === id}" aria-controls="agents-view" tabindex="${activeTab === id ? 0 : -1}">${label} <span class="ag-count">${count}</span></button>`).join('')}</div>
            <div class="ag-toolbar"><label class="ag-search"><span class="sr-only">Search ${activeTab === 'running' ? 'running agents' : activeTab === 'library' ? 'agent definitions' : 'agent types'}</span><span aria-hidden="true">⌕</span><input id="agents-search" type="search" placeholder="${activeTab === 'library' ? 'Search definitions by name, role, or project…' : activeTab === 'types' ? 'Search agent types…' : 'Search running agents…'}" value="${esc(searchText)}"></label>
            <div class="ag-refresh"><span id="agents-refresh-meta" class="ag-muted">${lastLoadedAt ? 'Updated ' + esc(new Date(lastLoadedAt).toLocaleTimeString()) : 'Loading…'}</span><button class="ag-button" id="agents-refresh-button" data-refresh-agents aria-label="Refresh agents">↻ Refresh</button></div></div>
            <section id="agents-view" role="tabpanel" aria-labelledby="agents-tab-${activeTab}" tabindex="0">${activeTab === 'running' ? renderRunning(list) : activeTab === 'library' ? renderLibrary(defs) : renderTypes(config, list)}</section>`;
        el.querySelectorAll('details[data-detail-key]').forEach(d => { d.open = openDetails.has(d.dataset.detailKey); });
        showScenarioMessage(feedback.message, feedback.error);
        if (focusedId) {
            const focus = document.getElementById(focusedId);
            focus?.focus({ preventScroll: true });
            if (selection !== null && focus?.setSelectionRange) focus.setSelectionRange(selection, selection);
        }
    }

    function renderCurrent() {
        if (configData) render(configData, agentsData, currentScenario, definitionsData, scenariosData, agentToolsInsight);
    }

    function showScenarioMessage(msg, isError) {
        feedback = { message: msg, error: isError };
        const node = document.getElementById('scenario-message');
        if (node) {
            node.textContent = msg;
            node.style.color = isError ? '#b42332' : '';
            node.setAttribute('role', isError ? 'alert' : 'status');
        }
    }

    async function fetchJson(url, errorKey, fallbackValue) {
        try {
            const res = await fetch(url);
            const data = await res.json().catch(() => null);
            if (!res.ok) {
                const msg =
                    (data && (data.message || data.errorMessage || data.title)) ||
                    'Request failed with status ' + res.status;
                setBackendError(errorKey, msg);
                return fallbackValue;
            }
            if (data === null && errorKey !== 'current-scenario-api') throw new Error('The server returned an empty response.');
            clearBackendError(errorKey);
            return data;
        } catch (e) {
            setBackendError(errorKey, e.message || 'Request failed');
            return fallbackValue;
        }
    }

    async function refreshRuntimeData(showInlineError, forceRender) {
        if (refreshBusy) return;
        refreshBusy = true;
        try {
            const [config, agents, current, defs, scenarios, toolUsage] = await Promise.all([
                fetchJson('/api/Config', 'config-api', configData),
                fetchJson('/api/agents', 'agents-api', agentsData),
                fetchJson('/api/Test/current-scenario', 'current-scenario-api', currentScenario),
                fetchJson('/api/agents/definitions', 'agent-definitions-api', definitionsData),
                fetchJson('/api/scenarios', 'scenarios-api', scenariosData),
                fetchJson('/api/agents/definitions/tool-usage', 'agent-tool-usage-api', agentToolsInsight)
            ]);

            if (config) configData = config;
            lastLoadedAt = Date.now();
            agentsData = Array.isArray(agents) ? agents : [];
            currentScenario = current;
            definitionsData = Array.isArray(defs) ? defs : [];
            scenariosData = Array.isArray(scenarios) ? scenarios : [];
            agentToolsInsight =
                toolUsage && typeof toolUsage === 'object' && Array.isArray(toolUsage.agents) ? toolUsage : { agents: [] };

            if (configData) {
                const nextSignature = buildRenderSignature(
                    configData,
                    agentsData,
                    currentScenario,
                    definitionsData,
                    scenariosData,
                    agentToolsInsight
                );
                if (forceRender || nextSignature !== lastRenderSignature) {
                    render(configData, agentsData, currentScenario, definitionsData, scenariosData, agentToolsInsight);
                    lastRenderSignature = nextSignature;
                }
            }

            const meta = document.getElementById('agents-refresh-meta');
            if (meta) meta.textContent = 'Updated ' + new Date(lastLoadedAt).toLocaleTimeString();
            if (showInlineError && backendErrors.size > 0) {
                showScenarioMessage('Some data could not be refreshed. See the issues above.', true);
            }
        } finally { refreshBusy = false; }
    }

    async function applyScenario() {
        if (mutationBusy || refreshBusy) return;
        mutationBusy = true;
        scenarioApplying = true;
        renderCurrent();
        showScenarioMessage('Applying scenario…', false);
        try {
            const select = document.getElementById('agents-scenario-select');
            // PRD-013 Phase 4: POST /api/scenarios/{id}/apply; id "default" → Agctor:Dashboard:ScenarioName
            const scenarioId = select && select.value ? select.value : 'default';
            const res = await fetch('/api/scenarios/' + encodeURIComponent(scenarioId) + '/apply', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ parameters: {} })
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok || data.success === false) {
                const msg = data.errorMessage || data.message || 'Failed to apply scenario.';
                setBackendError('scenario-apply', msg);
                await refreshRuntimeData(false, true);
                showScenarioMessage(msg, true);
                return;
            }
            clearBackendError('scenario-apply');
            const count = data.createdAgentIds ? data.createdAgentIds.length : 0;
            const successMsg = 'Scenario applied. ' + count + ' agent(s) reported created.';
            await refreshRuntimeData(false, true);
            showScenarioMessage(successMsg, false);
        } catch (e) {
            const msg = 'Error: ' + (e.message || 'Request failed.');
            setBackendError('scenario-apply', msg);
            await refreshRuntimeData(false, true);
            showScenarioMessage(msg, true);
        } finally {
            mutationBusy = false;
            scenarioApplying = false;
            const button = el.querySelector('[data-apply-scenario]');
            if (button) { button.disabled = !scenariosData.length; button.textContent = 'Apply scenario'; }
            renderCurrent();
        }
    }

    async function setAgentTypeEnabled(typeName, enabled) {
        const res = await fetch('/api/agents/types/' + encodeURIComponent(typeName) + '/enabled', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ enabled: enabled })
        });
        if (!res.ok) {
            const data = await res.json().catch(() => ({}));
            const msg = (data && (data.message || data.errorMessage)) || 'Failed to update enablement (' + res.status + ').';
            setBackendError('enable-' + typeName, msg);
            throw new Error(msg);
        }
        clearBackendError('enable-' + typeName);
    }

    el.addEventListener('click', function (e) {
        const t = e.target.closest('button');
        if (!t) return;
        if (t.dataset.agentTab) {
            activeTab = t.dataset.agentTab;
            searchText = '';
            renderCurrent();
            document.getElementById('agents-tab-' + activeTab)?.focus();
        }
        if (t.matches('[data-scenario-toggle]')) { scenarioExpanded = !scenarioExpanded; renderCurrent(); }
        if (t.matches('[data-clear-search]')) { searchText = ''; renderCurrent(); document.getElementById('agents-search')?.focus(); }
        if (t.matches('[data-apply-scenario]')) applyScenario();
        if (t.matches('[data-refresh-agents]') && !mutationBusy) refreshRuntimeData(true, true);
        if (t.matches('[data-yaml-new]')) openNewPmAgent();
        if (t.matches('[data-yaml-edit]')) {
            openEditPmAgent(t.dataset.yamlEdit).catch(() => showScenarioMessage('Could not open this definition. Please try again.', true));
        }
        if (t.matches('[data-yaml-del]')) deletePmAgent(t.dataset.yamlDel);
    });

    el.addEventListener('input', function (e) {
        if (e.target.id === 'agents-search') { searchText = e.target.value; renderCurrent(); }
    });
    el.addEventListener('keydown', function (e) {
        if (!e.target.matches('[role="tab"]')) return;
        const keys = ['running', 'library', 'types'];
        const index = keys.indexOf(activeTab);
        let next;
        if (e.key === 'ArrowRight') next = keys[(index + 1) % keys.length];
        if (e.key === 'ArrowLeft') next = keys[(index + keys.length - 1) % keys.length];
        if (e.key === 'Home') next = keys[0];
        if (e.key === 'End') next = keys[keys.length - 1];
        if (next) { e.preventDefault(); activeTab = next; searchText = ''; renderCurrent(); document.getElementById('agents-tab-' + activeTab).focus(); }
    });

    if (drawerHost) {
        drawerHost.addEventListener('click', function (e) {
            const t = e.target.closest('button');
            if (!t) return;
            if (t.matches('[data-agents-drawer-backdrop], [data-agents-drawer-close]')) closePmDrawer();
            if (t.matches('[data-pm-save]')) savePmAgent();
        });
        drawerHost.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') { e.preventDefault(); closePmDrawer(); }
            if (e.key !== 'Tab') return;
            const fields = Array.from(drawerPanel.querySelectorAll('button, a[href], input, textarea, select, [tabindex="0"]')).filter(x => !x.disabled && x.getClientRects().length);
            const first = fields[0], last = fields[fields.length - 1];
            if (e.shiftKey && (document.activeElement === first || document.activeElement === drawerPanel)) { e.preventDefault(); last?.focus(); }
            else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first?.focus(); }
        });
    }

    el.addEventListener('change', async function (e) {
        const input = e.target;
        if (input.id === 'agents-scenario-select') { selectedScenario = input.value; return; }
        if (!input.matches('input[data-agent-type-toggle]')) return;
        const typeName = input.dataset.agentTypeToggle;
        if (mutationBusy || refreshBusy) { input.checked = !input.checked; return; }
        const enabled = input.checked;
        const count = agentsData.filter(a => a.type === typeName).length;
        if (!enabled && count && !confirm('Disable ' + displayType(typeName) + '? This stops ' + count + ' registered instance(s) and prevents new ones. The setting is saved across restarts.')) { input.checked = true; return; }
        mutationBusy = true;
        input.disabled = true;
        try {
            await setAgentTypeEnabled(typeName, enabled);
            await refreshRuntimeData(false, true);
            showScenarioMessage(displayType(typeName) + (enabled ? ' is available. Apply a scenario to create instances.' : ' is disabled. Existing instances have been stopped.'), false);
        } catch {
            input.checked = !enabled;
            await refreshRuntimeData(true, true);
        } finally {
            mutationBusy = false;
            el.querySelectorAll('[data-agent-type-toggle]').forEach(x => { x.disabled = false; });
        }
    });

    fetchJson('/api/Config', 'config-api', null)
        .then(async (config) => {
            if (!config) {
                el.innerHTML =
                    '<div class="p-6 bg-red-50 border border-red-200 rounded-lg dark:bg-gray-800 dark:border-red-800"><p class="text-red-700 dark:text-red-400">Failed to load configuration.</p></div>';
                return;
            }

            configData = config;
            await refreshRuntimeData(false, true);

            if (refreshTimer) window.clearInterval(refreshTimer);
            refreshTimer = window.setInterval(function () {
                if (!mutationBusy && !document.hidden) refreshRuntimeData(false, false);
            }, 5000);
        })
        .catch(() => {
            el.innerHTML =
                '<div class="p-6 bg-red-50 border border-red-200 rounded-lg dark:bg-gray-800 dark:border-red-800"><p class="text-red-700 dark:text-red-400">Failed to load agents.</p></div>';
        });
})();
