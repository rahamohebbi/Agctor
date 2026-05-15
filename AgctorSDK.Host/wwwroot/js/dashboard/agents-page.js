/**
 * Dashboard Agents page (PRD-010): unified agent-type list, Flowbite-styled toggles,
 * single configured scenario Apply, and refresh.
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
    /** Prevents PUT loop when we refresh after toggle */
    let suppressToggleUntil = 0;

    /** PRD-013 Phase 2: create / edit project-memory YAML from unified Agents page */
    let pmDrawerMode = 'create'; // create | edit | view-csharp

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
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
            agents: Array.isArray(agents) ? agents : [],
            current: current || null,
            definitions: Array.isArray(definitions) ? definitions : [],
            scenarios: Array.isArray(scenarios) ? scenarios : [],
            agentTools: agentTools && Array.isArray(agentTools.agents) ? agentTools.agents : [],
            errors: stableErrorsSnapshot()
        });
    }

    function refreshMetaText(list) {
        const count = Array.isArray(list) ? list.length : 0;
        const loaded = lastLoadedAt ? new Date(lastLoadedAt).toLocaleTimeString() : 'Not loaded yet';
        return esc(String(count)) + ' active instance(s). Last refreshed at ' + esc(loaded) + '.';
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
        return { spec: spec, relativePath: rel || null };
    }

    function renderPmDrawerForm(spec, relativePath, title, readOnly) {
        if (!drawerPanel) return;
        const idReadonly = readOnly || pmDrawerMode === 'edit';
        drawerPanel.innerHTML =
            '<div class="p-6 space-y-4">' +
            '<div class="flex items-start justify-between gap-2">' +
            '<h2 class="text-lg font-semibold text-gray-900 dark:text-white">' +
            esc(title) +
            '</h2>' +
            '<button type="button" data-agents-drawer-close class="text-gray-500 hover:text-gray-800 dark:hover:text-gray-200 text-sm">Close</button>' +
            '</div>' +
            '<p class="text-xs text-gray-500 dark:text-gray-400">Saved files live under the configured project root (<code class="text-xs">Agctor:ProjectMemory:ProjectRoot</code>). For the full field set use <a class="text-blue-600 dark:text-blue-400 hover:underline" href="/Dashboard/ProjectMemory/Agents">Project Memory → Agents</a>.</p>' +
            '<div class="space-y-3">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Id</label>' +
            '<input id="agents-pm-id" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (idReadonly ? 'readonly' : '') +
            ' /></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Relative path (optional)</label>' +
            '<input id="agents-pm-relpath" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white" placeholder=".agctor/agents/people/…" ' +
            (readOnly ? 'readonly' : '') +
            ' /></div>' +
            '<div class="grid grid-cols-2 gap-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Name</label>' +
            '<input id="agents-pm-name" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            ' /></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Role</label>' +
            '<input id="agents-pm-role" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            ' /></div></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Description</label>' +
            '<textarea id="agents-pm-desc" rows="2" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            '></textarea></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Project types (comma-separated)</label>' +
            '<input id="agents-pm-pt" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            ' /></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Instructions (one per line)</label>' +
            '<textarea id="agents-pm-ins" rows="4" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            '></textarea></div>' +
            '<div class="grid grid-cols-2 gap-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Input type</label>' +
            '<input id="agents-pm-in" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            ' /></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Output type</label>' +
            '<input id="agents-pm-out" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            ' /></div></div>' +
            '<div class="grid grid-cols-2 gap-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Tools allow (lines)</label>' +
            '<textarea id="agents-pm-tallow" rows="3" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            '></textarea></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Tools deny (lines)</label>' +
            '<textarea id="agents-pm-tdeny" rows="3" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            '></textarea></div></div>' +
            '<div class="grid grid-cols-2 gap-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Memory read (lines)</label>' +
            '<textarea id="agents-pm-mr" rows="3" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            '></textarea></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Memory write (lines)</label>' +
            '<textarea id="agents-pm-mw" rows="3" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            '></textarea></div></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Guardrails (lines)</label>' +
            '<textarea id="agents-pm-g" rows="2" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (readOnly ? 'readonly' : '') +
            '></textarea></div>' +
            '</div>' +
            (readOnly
                ? ''
                : '<div class="flex flex-wrap gap-2 pt-2">' +
                  '<button type="button" data-pm-save class="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700">Save</button>' +
                  '<span id="agents-pm-msg" class="self-center text-sm text-gray-600 dark:text-gray-400"></span>' +
                  '</div>') +
            '</div>';

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
        drawerHost.classList.remove('hidden');
        drawerHost.setAttribute('aria-hidden', 'false');
    }

    async function openNewPmAgent() {
        pmDrawerMode = 'create';
        renderPmDrawerForm(normalizePmSpec(defaultPmSpec()), '', 'New project-memory agent', false);
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
        if (data.kind === 'csharp-type') {
            pmDrawerMode = 'view-csharp';
            const det = data.detail || {};
            const cTools = toolsForDefinitionId(data.id, agentToolsInsight);
            let toolsBlock = '';
            if (cTools.length) {
                toolsBlock =
                    '<dt class="text-gray-500 pt-2">Host tools</dt><dd class="mt-1 flex flex-wrap gap-1">' +
                    cTools
                        .map(function (t) {
                            const tip = [t.displayName || t.clrTypeName, t.description].filter(Boolean).join(' — ');
                            return (
                                '<span class="inline-flex rounded-md border border-gray-200 bg-gray-50 px-2 py-0.5 text-xs text-gray-800 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100" title="' +
                                esc(tip) +
                                '">' +
                                esc(t.displayName || t.clrTypeName) +
                                '</span>'
                            );
                        })
                        .join('') +
                    '</dd>';
            } else {
                toolsBlock =
                    '<dt class="text-gray-500 pt-2">Host tools</dt><dd class="text-xs text-gray-500 dark:text-gray-400">None resolved for this type (see Tool access section).</dd>';
            }
            if (!drawerPanel) return;
            drawerPanel.innerHTML =
                '<div class="p-6 space-y-4">' +
                '<div class="flex items-start justify-between gap-2">' +
                '<h2 class="text-lg font-semibold text-gray-900 dark:text-white">C# type: ' +
                esc(data.id) +
                '</h2>' +
                '<button type="button" data-agents-drawer-close class="text-gray-500 hover:text-gray-800 dark:hover:text-gray-200 text-sm">Close</button>' +
                '</div>' +
                '<p class="text-sm text-gray-600 dark:text-gray-300">This agent is defined in code. Enable or disable it using the toggles in the runtime table above.</p>' +
                '<dl class="text-sm space-y-2">' +
                '<dt class="text-gray-500">CLR type</dt><dd class="font-mono text-gray-900 dark:text-white">' +
                esc(det.clrType || '') +
                '</dd>' +
                '<dt class="text-gray-500">Enabled</dt><dd>' +
                esc(String(det.enabled !== false)) +
                '</dd>' +
                toolsBlock +
                '</dl>' +
                '</div>';
            openPmDrawer();
            return;
        }
        if (data.kind !== 'project-memory-yaml' || !data.detail || !data.detail.spec) {
            alert('Unexpected response shape.');
            return;
        }
        pmDrawerMode = 'edit';
        renderPmDrawerForm(normalizePmSpec(data.detail.spec), data.detail.relativePath || '', 'Edit: ' + id, false);
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

    /** Group GET /api/agents by CLR type name */
    function groupAgentsByType(agents) {
        const map = new Map();
        for (const a of agents) {
            const t = a.type || 'Unknown';
            if (!map.has(t)) map.set(t, []);
            map.get(t).push(a);
        }
        return map;
    }

    /** Tools linked to a C# agent type (same keys as runtime toggles). */
    function csharpToolsForType(typeName, insight) {
        if (!insight || !Array.isArray(insight.agents)) return [];
        const row = insight.agents.find(function (a) {
            return a.kind === 'csharp-agent-type' && a.agentId === typeName;
        });
        return row && Array.isArray(row.tools) ? row.tools : [];
    }

    /** Card grid: each agent with tool chips + optional unmapped YAML tokens. */
    function renderAgentToolsSection(insight) {
        const errKey = 'agent-tool-usage-api';
        const err = backendErrors.has(errKey) ? backendErrors.get(errKey) : null;
        if (err) {
            return (
                '<section class="mb-6 p-4 rounded-xl border border-red-200 bg-red-50/90 dark:bg-red-900/20 dark:border-red-800">' +
                '<h2 class="text-sm font-semibold text-red-900 dark:text-red-100">Tool access by agent</h2>' +
                '<p class="mt-2 text-sm text-red-800 dark:text-red-200">' +
                esc(err) +
                '</p></section>'
            );
        }
        const agents = insight && Array.isArray(insight.agents) ? insight.agents : [];
        if (!agents.length) {
            return (
                '<section class="mb-6 p-6 rounded-xl border border-dashed border-gray-300 bg-gray-50/80 dark:border-gray-600 dark:bg-gray-800/50">' +
                '<h2 class="text-base font-semibold text-gray-900 dark:text-white">Tool access by agent</h2>' +
                '<p class="mt-2 text-sm text-gray-600 dark:text-gray-400">No mappings returned yet. Open a project with agent YAML or ensure tool actors are registered.</p></section>'
            );
        }
        const cards = agents
            .map(function (a) {
                const isYaml = a.kind === 'project-memory-yaml';
                const kindBadge =
                    '<span class="shrink-0 inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide ' +
                    (isYaml
                        ? 'bg-amber-100 text-amber-900 dark:bg-amber-900/50 dark:text-amber-100'
                        : 'bg-emerald-100 text-emerald-900 dark:bg-emerald-900/50 dark:text-emerald-100') +
                    '">' +
                    (isYaml ? 'YAML' : 'C#') +
                    '</span>';
                const tools = Array.isArray(a.tools) ? a.tools : [];
                const pills =
                    tools.length === 0
                        ? ''
                        : '<div class="mt-3 flex flex-wrap gap-1.5" role="list">' +
                          tools
                              .map(function (t) {
                                  const tip = [t.displayName || t.clrTypeName, t.description, t.detail].filter(Boolean).join(' — ');
                                  const rest = t.httpPrimaryId
                                      ? '<span class="ml-1 text-[10px] opacity-75 font-normal">' + esc(t.httpPrimaryId) + '</span>'
                                      : '';
                                  return (
                                      '<span role="listitem" class="inline-flex max-w-full items-center rounded-lg border border-gray-200 bg-white px-2 py-1 text-xs font-medium text-gray-800 shadow-sm dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100" title="' +
                                      esc(tip) +
                                      '">' +
                                      '<span class="truncate">' +
                                      esc(t.displayName || t.clrTypeName) +
                                      '</span>' +
                                      rest +
                                      '</span>'
                                  );
                              })
                              .join('') +
                          '</div>';
                const unmapped = Array.isArray(a.unmappedYamlAllowTokens) ? a.unmappedYamlAllowTokens : [];
                const unmappedBlock =
                    unmapped.length === 0
                        ? ''
                        : '<div class="mt-3 rounded-lg border border-amber-200/80 bg-amber-50/60 px-2.5 py-2 dark:border-amber-800/60 dark:bg-amber-900/20">' +
                          '<p class="text-[11px] font-medium text-amber-900 dark:text-amber-100">Allow-list tokens not mapped to a host tool</p>' +
                          '<div class="mt-1 flex flex-wrap gap-1">' +
                          unmapped
                              .map(function (tok) {
                                  return '<code class="rounded bg-white/80 px-1.5 py-0.5 text-[10px] text-amber-950 dark:bg-gray-900 dark:text-amber-100">' + esc(tok) + '</code>';
                              })
                              .join('') +
                          '</div></div>';
                const foot =
                    tools.length === 0 && unmapped.length === 0
                        ? '<p class="mt-3 text-xs text-gray-500 dark:text-gray-400">No host tools linked for this agent.</p>'
                        : '';
                return (
                    '<article class="flex flex-col rounded-xl border border-gray-200 bg-white p-4 shadow-sm transition-shadow hover:shadow-md dark:border-gray-700 dark:bg-gray-800/90">' +
                    '<div class="flex items-start justify-between gap-2">' +
                    '<div class="min-w-0">' +
                    '<h3 class="truncate text-sm font-semibold text-gray-900 dark:text-white" title="' +
                    esc(a.agentLabel || a.agentId) +
                    '">' +
                    esc(a.agentLabel || a.agentId) +
                    '</h3>' +
                    '<p class="mt-0.5 truncate font-mono text-[11px] text-gray-500 dark:text-gray-400">' +
                    esc(a.agentId) +
                    '</p></div>' +
                    kindBadge +
                    '</div>' +
                    pills +
                    unmappedBlock +
                    foot +
                    '</article>'
                );
            })
            .join('');
        return (
            '<section class="mb-6" aria-labelledby="agent-tools-heading">' +
            '<div class="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">' +
            '<div>' +
            '<h2 id="agent-tools-heading" class="text-base font-semibold text-gray-900 dark:text-white">Tool access by agent</h2>' +
            '<p class="mt-1 max-w-3xl text-xs leading-relaxed text-gray-600 dark:text-gray-400">Host tools each agent may use, derived dynamically from project-memory <span class="font-mono text-[10px]">tools.allow</span> and known C# routing. Open the Tools page for the inverse view (tool → agents).</p>' +
            '</div>' +
            '<a href="/Dashboard/Tools" class="inline-flex items-center justify-center rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm font-medium text-gray-800 shadow-sm hover:bg-gray-50 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-100 dark:hover:bg-gray-700">Tools dashboard</a>' +
            '</div>' +
            '<div class="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-3">' +
            cards +
            '</div></section>'
        );
    }

    /** Lookup tool list for a definition id (YAML id or C# type name). */
    function toolsForDefinitionId(defId, insight) {
        if (!insight || !Array.isArray(insight.agents)) return [];
        const row = insight.agents.find(function (a) {
            return a.agentId === defId;
        });
        return row && Array.isArray(row.tools) ? row.tools : [];
    }

    function renderDefinitionToolsCell(defId, insight) {
        const tools = toolsForDefinitionId(defId, insight);
        if (!tools.length) {
            return '<td class="px-6 py-4 text-xs text-gray-400 dark:text-gray-500">—</td>';
        }
        const title = tools
            .map(function (t) {
                return t.displayName || t.clrTypeName;
            })
            .join(', ');
        const preview = tools
            .slice(0, 2)
            .map(function (t) {
                return esc(t.displayName || t.clrTypeName);
            })
            .join('<span class="text-gray-300 dark:text-gray-600"> · </span>');
        const more = tools.length > 2 ? ' <span class="text-gray-400">+' + String(tools.length - 2) + '</span>' : '';
        return (
            '<td class="px-6 py-4 text-xs text-gray-800 dark:text-gray-200 max-w-[10rem]" title="' +
            esc(title) +
            '">' +
            preview +
            more +
            '</td>'
        );
    }

    function render(config, agents, current, definitions, scenarios, agentTools) {
        const list = Array.isArray(agents) ? agents : [];
        const defs = Array.isArray(definitions) ? definitions : [];
        const scs = Array.isArray(scenarios) ? scenarios : [];
        const agentTypes = config.agentTypes && typeof config.agentTypes === 'object' ? config.agentTypes : {};
        const enablement = config.agentTypeEnablement && typeof config.agentTypeEnablement === 'object'
            ? config.agentTypeEnablement
            : {};
        const defaultScenarioName = config.dashboardScenarioName || '';
        const selectedScenarioName = (current && current.scenarioName) || defaultScenarioName;
        const byType = groupAgentsByType(list);
        const typeKeys = Object.keys(agentTypes).sort((a, b) => a.localeCompare(b));

        const errorBlock = renderBackendErrors();
        const currentBlock = current
            ? '<div class="p-4 mb-4 rounded-lg bg-amber-50 border border-amber-200 dark:bg-amber-900/20 dark:border-amber-800"><p class="text-sm font-medium text-amber-800 dark:text-amber-200">Current scenario</p><p class="mt-1 font-semibold text-amber-900 dark:text-white">' +
              esc(current.scenarioName) + '</p>' +
              (current.description ? '<p class="mt-1 text-sm text-amber-700 dark:text-amber-300">' + esc(current.description) + '</p>' : '') +
              '</div>'
            : '<div class="p-4 mb-4 rounded-lg bg-gray-50 border border-gray-200 dark:bg-gray-800 dark:border-gray-700"><p class="text-sm text-gray-500 dark:text-gray-400">No scenario applied in this session yet. Use Apply to run the configured scenario.</p></div>';

        let rows = '';
        for (const typeName of typeKeys) {
            const enabled = enablement[typeName] !== false;
            const instances = byType.get(typeName) || [];
            const ctTools = csharpToolsForType(typeName, agentTools);
            let csharpToolsHtml = '';
            if (ctTools.length) {
                csharpToolsHtml =
                    '<div class="mt-2 flex max-w-md flex-wrap gap-1" role="list" aria-label="Host tools for this type">' +
                    ctTools
                        .map(function (t) {
                            const tip = [t.displayName || t.clrTypeName, t.description].filter(Boolean).join(' — ');
                            return (
                                '<span role="listitem" class="inline-flex max-w-full items-center rounded-md border border-indigo-100 bg-indigo-50/90 px-1.5 py-0.5 text-[11px] font-medium text-indigo-900 dark:border-indigo-800 dark:bg-indigo-950/40 dark:text-indigo-100" title="' +
                                esc(tip) +
                                '">' +
                                '<span class="truncate">' +
                                esc(t.displayName || t.clrTypeName) +
                                '</span></span>'
                            );
                        })
                        .join('') +
                    '</div>';
            }
            const countBadge =
                '<span class="bg-gray-100 text-gray-800 text-xs font-medium px-2.5 py-0.5 rounded dark:bg-gray-700 dark:text-gray-300">' +
                instances.length +
                '</span>';
            let instLinks = '';
            if (instances.length) {
                instLinks =
                    '<div class="flex flex-wrap gap-1 mt-1">' +
                    instances
                        .map(
                            (a) =>
                                '<a href="/Dashboard/AgentDetail/' +
                                encodeURIComponent(a.id) +
                                '" class="text-xs font-medium text-blue-600 dark:text-blue-400 hover:underline">' +
                                esc(a.id) +
                                '</a>'
                        )
                        .join('') +
                    '</div>';
            } else {
                instLinks = '<span class="text-xs text-gray-400 dark:text-gray-500">None running</span>';
            }

            const toggleId = 'toggle-' + typeName.replace(/[^a-zA-Z0-9_-]/g, '_');
            rows +=
                '<tr class="bg-white border-b dark:bg-gray-800 dark:border-gray-700">' +
                '<th scope="row" class="px-6 py-4 font-medium text-gray-900 whitespace-nowrap dark:text-white">' +
                esc(typeName) +
                csharpToolsHtml +
                '</th>' +
                '<td class="px-6 py-4 text-gray-500 dark:text-gray-400 text-xs break-all max-w-md">' +
                esc(agentTypes[typeName] || '') +
                '</td>' +
                '<td class="px-6 py-4">' +
                countBadge +
                '</td>' +
                '<td class="px-6 py-4">' +
                '<label class="inline-flex items-center cursor-pointer">' +
                '<input type="checkbox" class="sr-only peer" data-agent-type-toggle="' +
                esc(typeName) +
                '" id="' +
                toggleId +
                '" ' +
                (enabled ? 'checked' : '') +
                '>' +
                '<div class="relative w-11 h-6 bg-gray-200 peer-focus:outline-none peer-focus:ring-4 peer-focus:ring-blue-300 dark:peer-focus:ring-blue-800 rounded-full peer dark:bg-gray-700 peer-checked:after:translate-x-full rtl:peer-checked:after:-translate-x-full peer-checked:after:border-white after:content-[\'\'] after:absolute after:top-[2px] after:start-[2px] after:bg-white after:border-gray-300 after:border after:rounded-full after:h-5 after:w-5 after:transition-all dark:border-gray-600 peer-checked:bg-blue-600"></div>' +
                '<span class="ms-3 text-sm font-medium text-gray-900 dark:text-gray-300">' +
                (enabled ? 'Enabled' : 'Disabled') +
                '</span>' +
                '</label>' +
                '</td>' +
                '<td class="px-6 py-4">' +
                instLinks +
                '</td>' +
                '</tr>';
        }

        const runtimeCapableCount = defs.filter(function (d) {
            return d.kind === 'csharp-type';
        }).length;
        const nonRuntimeCount = defs.length - runtimeCapableCount;

        let defRows = '';
        defs.forEach(function (d) {
            const meta = d.metadata ? JSON.stringify(d.metadata) : '';
            const safeId = esc(d.id || '');
            const runtimeBadge =
                d.kind === 'project-memory-yaml'
                    ? '<span class="inline-flex items-center rounded bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-200 px-2 py-0.5 text-[11px] font-medium">Non-runtime definition (YAML)</span>'
                    : '<span class="inline-flex items-center rounded bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-200 px-2 py-0.5 text-[11px] font-medium">Runtime-capable type (C#)</span>';
            const yamlActions =
                d.kind === 'project-memory-yaml'
                    ? '<button type="button" class="text-blue-600 dark:text-blue-400 hover:underline text-xs mr-2" data-yaml-edit="' +
                      safeId +
                      '">Edit</button>' +
                      '<button type="button" class="text-red-600 dark:text-red-400 hover:underline text-xs" data-yaml-del="' +
                      safeId +
                      '">Delete</button>'
                    : '<button type="button" class="text-blue-600 dark:text-blue-400 hover:underline text-xs" data-csharp-view="' +
                      safeId +
                      '">View</button>';
            defRows +=
                '<tr class="bg-white border-b dark:bg-gray-800 dark:border-gray-700">' +
                '<th scope="row" class="px-6 py-4 font-medium text-gray-900 whitespace-nowrap dark:text-white">' + esc(d.displayName || d.id) + '</th>' +
                '<td class="px-6 py-4 text-xs text-gray-600 dark:text-gray-300">' + safeId + '</td>' +
                '<td class="px-6 py-4 text-xs">' + esc(d.kind || '') + '</td>' +
                '<td class="px-6 py-4 text-xs font-mono break-all max-w-md">' + esc(d.source || '') + '</td>' +
                '<td class="px-6 py-4 text-xs">' + esc(d.state || '') + '</td>' +
                '<td class="px-6 py-4 text-xs">' + runtimeBadge + '</td>' +
                renderDefinitionToolsCell(d.id, agentTools) +
                '<td class="px-6 py-4 text-xs break-all max-w-md">' + esc(meta) + '</td>' +
                '<td class="px-6 py-4 text-xs whitespace-nowrap">' +
                yamlActions +
                '</td>' +
                '</tr>';
        });
        if (!defRows) {
            defRows =
                '<tr class="bg-white border-b dark:bg-gray-800 dark:border-gray-700">' +
                '<td class="px-6 py-4 text-xs text-gray-500 dark:text-gray-400" colspan="9">No definitions found.</td>' +
                '</tr>';
        }

        const scenarioOptions = scs
            .map(function (s) {
                const id = s.id || '';
                const text = (s.displayName || id) + ' [' + id + ']';
                return '<option value="' + esc(id) + '" ' + (id === selectedScenarioName ? 'selected' : '') + '>' + esc(text) + '</option>';
            })
            .join('');

        el.innerHTML =
            errorBlock +
            currentBlock +
            '<div class="p-6 bg-white border border-gray-200 rounded-lg shadow dark:bg-gray-800 dark:border-gray-700 mb-6">' +
            '<div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">' +
            '<div>' +
            '<h2 class="text-lg font-semibold text-gray-900 dark:text-white">Scenario</h2>' +
            '<p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Default startup scenario remains <strong class="text-gray-800 dark:text-gray-200">' +
            esc(defaultScenarioName) +
            '</strong>. You can apply a different scenario below for this session.</p>' +
            '</div>' +
            '<div class="flex flex-wrap items-center gap-2">' +
            '<select id="agents-scenario-select" class="min-w-[14rem] py-2.5 px-3 text-sm font-medium text-gray-900 bg-white rounded-lg border border-gray-200 dark:bg-gray-800 dark:text-gray-200 dark:border-gray-600">' +
            scenarioOptions +
            '</select>' +
            '<div class="flex flex-wrap gap-2">' +
            '<button type="button" data-apply-scenario class="text-white bg-blue-700 hover:bg-blue-800 focus:ring-4 focus:ring-blue-300 font-medium rounded-lg text-sm px-5 py-2.5 dark:bg-blue-600 dark:hover:bg-blue-700 focus:outline-none dark:focus:ring-blue-800">' +
            'Apply scenario' +
            '</button>' +
            '<button type="button" data-refresh-agents class="py-2.5 px-5 text-sm font-medium text-gray-900 focus:outline-none bg-white rounded-lg border border-gray-200 hover:bg-gray-100 hover:text-blue-700 focus:z-10 focus:ring-4 focus:ring-gray-100 dark:focus:ring-gray-700 dark:bg-gray-800 dark:text-gray-400 dark:border-gray-600 dark:hover:text-white dark:hover:bg-gray-700">' +
            'Refresh' +
            '</button>' +
            '</div></div></div>' +
            '<div id="scenario-message" class="mt-3 min-h-[1.5rem] text-sm"></div></div>' +
            '<div class="relative overflow-x-auto shadow-md sm:rounded-lg">' +
            '<table class="w-full text-sm text-left rtl:text-right text-gray-500 dark:text-gray-400">' +
            '<thead class="text-xs text-gray-700 uppercase bg-gray-50 dark:bg-gray-700 dark:text-gray-400">' +
            '<tr>' +
            '<th scope="col" class="px-6 py-3">Type</th>' +
            '<th scope="col" class="px-6 py-3">Registered CLR</th>' +
            '<th scope="col" class="px-6 py-3">Instances</th>' +
            '<th scope="col" class="px-6 py-3">Enabled</th>' +
            '<th scope="col" class="px-6 py-3">Runtime</th>' +
            '</tr></thead><tbody>' +
            rows +
            '</tbody></table></div>' +
            renderAgentToolsSection(agentTools) +
            '<div class="mt-4 p-4 rounded-lg bg-indigo-50 border border-indigo-200 dark:bg-indigo-900/20 dark:border-indigo-800">' +
            '<p class="text-sm font-medium text-indigo-900 dark:text-indigo-100">Runtime vs non-runtime definitions</p>' +
            '<p class="mt-1 text-xs text-indigo-800 dark:text-indigo-200">' +
            'Runtime-capable C# types can be spawned as actor instances by scenarios/tools. YAML project-memory definitions are configuration specs used by project-memory pipelines and do not appear as running actors by themselves.' +
            '</p>' +
            '<p class="mt-2 text-xs text-indigo-800 dark:text-indigo-200">' +
            'Runtime-capable: <strong>' + esc(String(runtimeCapableCount)) + '</strong> · Non-runtime YAML: <strong>' + esc(String(nonRuntimeCount)) + '</strong>' +
            '</p></div>' +
            '<div class="mt-6 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">' +
            '<h2 class="text-base font-semibold text-gray-900 dark:text-white">Agent definitions</h2>' +
            '<button type="button" data-yaml-new class="self-start text-white bg-blue-700 hover:bg-blue-800 focus:ring-4 focus:ring-blue-300 font-medium rounded-lg text-sm px-4 py-2 dark:bg-blue-600 dark:hover:bg-blue-700 focus:outline-none dark:focus:ring-blue-800">' +
            'New project-memory agent' +
            '</button></div>' +
            '<div class="mt-2 relative overflow-x-auto shadow-md sm:rounded-lg">' +
            '<table class="w-full text-sm text-left rtl:text-right text-gray-500 dark:text-gray-400">' +
            '<thead class="text-xs text-gray-700 uppercase bg-gray-50 dark:bg-gray-700 dark:text-gray-400">' +
            '<tr>' +
            '<th scope="col" class="px-6 py-3">Definition</th>' +
            '<th scope="col" class="px-6 py-3">Id</th>' +
            '<th scope="col" class="px-6 py-3">Kind</th>' +
            '<th scope="col" class="px-6 py-3">Source</th>' +
            '<th scope="col" class="px-6 py-3">State</th>' +
            '<th scope="col" class="px-6 py-3">Runtime</th>' +
            '<th scope="col" class="px-6 py-3">Host tools</th>' +
            '<th scope="col" class="px-6 py-3">Metadata</th>' +
            '<th scope="col" class="px-6 py-3">Actions</th>' +
            '</tr></thead><tbody>' +
            defRows +
            '</tbody></table></div>' +
            '<p class="mt-4 text-xs text-gray-500 dark:text-gray-400">' +
            refreshMetaText(list) +
            '</p>';
    }

    function showScenarioMessage(msg, isError) {
        const msgEl = document.getElementById('scenario-message');
        if (!msgEl) return;
        msgEl.textContent = msg;
        msgEl.className =
            'mt-3 min-h-[1.5rem] text-sm ' +
            (isError ? 'text-red-600 dark:text-red-400' : 'text-green-600 dark:text-green-400');
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
            clearBackendError(errorKey);
            return data;
        } catch (e) {
            setBackendError(errorKey, e.message || 'Request failed');
            return fallbackValue;
        }
    }

    async function refreshRuntimeData(showInlineError, forceRender) {
        const [agents, current, defs, scenarios, toolUsage] = await Promise.all([
            fetchJson('/api/agents', 'agents-api', agentsData),
            fetchJson('/api/Test/current-scenario', 'current-scenario-api', currentScenario),
            fetchJson('/api/agents/definitions', 'agent-definitions-api', definitionsData),
            fetchJson('/api/scenarios', 'scenarios-api', scenariosData),
            fetchJson('/api/agents/definitions/tool-usage', 'agent-tool-usage-api', agentToolsInsight)
        ]);

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
                lastLoadedAt = Date.now();
                render(configData, agentsData, currentScenario, definitionsData, scenariosData, agentToolsInsight);
                lastRenderSignature = nextSignature;
            }
        }

        if (showInlineError && backendErrors.size > 0) {
            showScenarioMessage('One or more backend issues were detected. See the red panel above.', true);
        }
    }

    async function applyScenario() {
        showScenarioMessage('Applying...', false);
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
        const t = e.target;
        if (!t || !t.matches) return;
        if (t.matches('button[data-apply-scenario]')) applyScenario();
        if (t.matches('button[data-refresh-agents]')) refreshRuntimeData(true, true);
        if (t.matches('button[data-yaml-new]')) openNewPmAgent();
        if (t.matches('button[data-yaml-edit]')) {
            const id = t.getAttribute('data-yaml-edit');
            if (id) openEditPmAgent(id);
        }
        if (t.matches('button[data-csharp-view]')) {
            const id = t.getAttribute('data-csharp-view');
            if (id) openEditPmAgent(id);
        }
        if (t.matches('button[data-yaml-del]')) {
            const id = t.getAttribute('data-yaml-del');
            if (id) deletePmAgent(id);
        }
    });

    if (drawerHost) {
        drawerHost.addEventListener('click', function (e) {
            const t = e.target;
            if (!t || !t.matches) return;
            if (t.matches('[data-agents-drawer-backdrop]') || t.matches('[data-agents-drawer-close]')) closePmDrawer();
            if (t.matches('[data-pm-save]')) savePmAgent();
        });
    }

    el.addEventListener('change', async function (e) {
        const input = e.target;
        if (!input || !input.matches || !input.matches('input[data-agent-type-toggle]')) return;
        const typeName = input.getAttribute('data-agent-type-toggle');
        if (!typeName) return;
        if (Date.now() < suppressToggleUntil) return;
        const enabled = input.checked;
        try {
            await setAgentTypeEnabled(typeName, enabled);
            const cfg = await fetchJson('/api/Config', 'config-api', configData);
            if (cfg) configData = cfg;
            suppressToggleUntil = Date.now() + 400;
            await refreshRuntimeData(false, true);
        } catch {
            input.checked = !enabled;
            await refreshRuntimeData(true, true);
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
                refreshRuntimeData(false, false);
            }, 5000);
        })
        .catch(() => {
            el.innerHTML =
                '<div class="p-6 bg-red-50 border border-red-200 rounded-lg dark:bg-gray-800 dark:border-red-800"><p class="text-red-700 dark:text-red-400">Failed to load agents.</p></div>';
        });
})();
