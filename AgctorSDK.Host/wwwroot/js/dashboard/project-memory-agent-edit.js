/**
 * Agent Studio: load/save AgentDefinitionSpec via project-memory API; form + server YAML preview tab.
 */
(function () {
    const root = document.getElementById('pm-agent-edit-root');
    const titleEl = document.getElementById('pm-edit-title');
    if (!root) return;

    const params = new URLSearchParams(window.location.search);
    const queryId = (params.get('id') || '').trim();

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
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

    function defaultSpec() {
        return {
            id: '',
            name: '',
            role: '',
            description: '',
            projectTypes: [],
            toolBundles: null,
            instructions: [],
            input: { type: '' },
            output: { type: '' },
            tools: { allow: [], deny: [] },
            memoryAccess: { read: [], write: [] },
            guardrails: [],
            runtimeHints: null
        };
    }

    function normalizeSpec(raw) {
        const s = raw && typeof raw === 'object' ? raw : {};
        const d = defaultSpec();
        d.id = typeof s.id === 'string' ? s.id : d.id;
        d.name = typeof s.name === 'string' ? s.name : d.name;
        d.role = typeof s.role === 'string' ? s.role : d.role;
        d.description = typeof s.description === 'string' ? s.description : d.description;
        d.projectTypes = Array.isArray(s.projectTypes) ? s.projectTypes.slice() : d.projectTypes;
        d.toolBundles = Array.isArray(s.toolBundles) ? s.toolBundles.slice() : s.toolBundles == null ? null : [];
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
        if (s.runtimeHints && typeof s.runtimeHints === 'object') {
            d.runtimeHints = {
                preferredModel: s.runtimeHints.preferredModel || null,
                preferredMode: s.runtimeHints.preferredMode || null
            };
        }
        return d;
    }

    function readForm() {
        const id = /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-id')).value.trim();
        const rel = /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-relpath')).value.trim();
        const spec = normalizeSpec({});
        spec.id = id;
        spec.name = /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-name')).value.trim();
        spec.role = /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-role')).value.trim();
        spec.description = /** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-desc')).value;
        spec.projectTypes = splitComma(/** @type {HTMLInputElement} */ (document.getElementById('pm-spec-pt')).value);
        const tb = /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-tb')).value.trim();
        spec.toolBundles = tb ? splitComma(tb) : null;
        spec.instructions = linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-ins')).value);
        spec.input = { type: /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-in')).value.trim() };
        spec.output = { type: /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-out')).value.trim() };
        spec.tools = {
            allow: collectToolsAllow(),
            deny: collectToolsDeny()
        };
        spec.memoryAccess = {
            read: linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-mr')).value),
            write: linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-mw')).value)
        };
        spec.guardrails = linesToArr(/** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-g')).value);
        const pm = /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-pm')).value.trim();
        const pmode = /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-pmode')).value.trim();
        spec.runtimeHints = pm || pmode ? { preferredModel: pm || null, preferredMode: pmode || null } : null;
        return { spec: spec, relativePath: rel || null };
    }

    var yamlPreview = '';
    var isExisting = false;

    function collectToolsAllow() {
        const panel = document.getElementById('pm-spec-tools-panel');
        if (panel && window.AgctorPersonaToolsUi) {
            return window.AgctorPersonaToolsUi.collectAllowFromEditor(panel);
        }
        return [];
    }

    function collectToolsDeny() {
        const panel = document.getElementById('pm-spec-tools-panel');
        if (panel && window.AgctorPersonaToolsUi) {
            return window.AgctorPersonaToolsUi.collectDenyFromEditor(panel);
        }
        return [];
    }

    function loadToolsPanel(personaId) {
        const panel = document.getElementById('pm-spec-tools-panel');
        if (!panel || !window.AgctorPersonaToolsUi) return;
        const pid = String(personaId || '').trim();
        if (!pid) {
            panel.innerHTML =
                '<p class="text-xs text-gray-500 dark:text-gray-400">Enter an agent id to load the tool catalog.</p>';
            return;
        }
        panel.innerHTML = '<p class="text-xs text-gray-500 dark:text-gray-400">Loading tools…</p>';
        window.AgctorPersonaToolsUi.fetchForPersona(pid, true)
            .then(function (dto) {
                window.AgctorPersonaToolsUi.renderAgentStudioTools(panel, dto || { hostTools: [], semanticTools: [], customAllowTokens: [], yamlDeny: [] });
            })
            .catch(function (e) {
                panel.innerHTML =
                    '<p class="text-xs text-red-600 dark:text-red-400">Failed to load tools: ' + esc(e.message || e) + '</p>';
            });
    }

    function renderForm() {
        root.innerHTML =
            '<div class="flex gap-2 border-b border-gray-200 dark:border-gray-700 mb-4">' +
            '<button type="button" class="pm-tab px-4 py-2 text-sm font-medium rounded-t-lg bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-100" data-t="form">Form</button>' +
            '<button type="button" class="pm-tab px-4 py-2 text-sm font-medium rounded-t-lg text-gray-600 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-gray-700" data-t="yaml">YAML preview</button>' +
            '</div>' +
            '<div id="pm-pane-form" class="space-y-6">' +
            '<div class="grid gap-4 md:grid-cols-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Id</label>' +
            '<input id="pm-spec-id" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" ' +
            (isExisting ? 'readonly' : '') +
            ' /></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Relative path (optional)</label>' +
            '<input id="pm-spec-relpath" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm font-mono text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" placeholder=".agctor/agents/people/…" /></div>' +
            '</div>' +
            '<div class="grid gap-4 md:grid-cols-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Name</label>' +
            '<input id="pm-spec-name" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" /></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Role</label>' +
            '<input id="pm-spec-role" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" /></div>' +
            '</div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Description</label>' +
            '<textarea id="pm-spec-desc" rows="2" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white"></textarea></div>' +
            '<div class="grid gap-4 md:grid-cols-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Project types (comma-separated)</label>' +
            '<input id="pm-spec-pt" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" /></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Tool bundles (comma, optional)</label>' +
            '<input id="pm-spec-tb" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" /></div>' +
            '</div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Instructions (one per line)</label>' +
            '<textarea id="pm-spec-ins" rows="4" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white"></textarea></div>' +
            '<div class="grid gap-4 md:grid-cols-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Input contract type</label>' +
            '<input id="pm-spec-in" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" /></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Output contract type</label>' +
            '<input id="pm-spec-out" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" /></div>' +
            '</div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Tools</label>' +
            '<div id="pm-spec-tools-panel"></div></div>' +
            '<div class="grid gap-4 md:grid-cols-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Memory read (one per line)</label>' +
            '<textarea id="pm-spec-mr" rows="3" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white"></textarea></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Memory write (one per line)</label>' +
            '<textarea id="pm-spec-mw" rows="3" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white"></textarea></div>' +
            '</div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Guardrails (one per line)</label>' +
            '<textarea id="pm-spec-g" rows="2" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-xs font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-white"></textarea></div>' +
            '<div class="grid gap-4 md:grid-cols-2">' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Preferred model (hint)</label>' +
            '<input id="pm-spec-pm" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" /></div>' +
            '<div><label class="block text-xs font-medium text-gray-700 dark:text-gray-300 mb-1">Preferred mode (hint)</label>' +
            '<input id="pm-spec-pmode" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" /></div>' +
            '</div>' +
            '<div class="flex gap-2">' +
            '<button type="button" id="pm-save" class="px-4 py-2.5 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700">Save</button>' +
            '<span id="pm-save-msg" class="self-center text-sm text-gray-600 dark:text-gray-400"></span>' +
            '</div></div>' +
            '<div id="pm-pane-yaml" class="hidden">' +
            '<pre id="pm-yaml-pre" class="p-4 text-xs font-mono bg-gray-900 text-green-100 rounded-lg overflow-auto max-h-[70vh] whitespace-pre-wrap"></pre></div>';

        const spec = window.__pmSpec;
        /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-id')).value = spec.id;
        /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-relpath')).value = window.__pmRel || '';
        /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-name')).value = spec.name;
        /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-role')).value = spec.role;
        /** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-desc')).value = spec.description;
        /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-pt')).value = joinComma(spec.projectTypes);
        /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-tb')).value = spec.toolBundles ? joinComma(spec.toolBundles) : '';
        /** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-ins')).value = arrToLines(spec.instructions);
        /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-in')).value = spec.input.type;
        /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-out')).value = spec.output.type;
        loadToolsPanel(spec.id);
        const idInput = /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-id'));
        if (idInput && !isExisting) {
            idInput.addEventListener('change', function () {
                loadToolsPanel(idInput.value.trim());
            });
        }
        /** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-mr')).value = arrToLines(spec.memoryAccess.read);
        /** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-mw')).value = arrToLines(spec.memoryAccess.write);
        /** @type {HTMLTextAreaElement} */ (document.getElementById('pm-spec-g')).value = arrToLines(spec.guardrails);
        if (spec.runtimeHints) {
            /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-pm')).value = spec.runtimeHints.preferredModel || '';
            /** @type {HTMLInputElement} */ (document.getElementById('pm-spec-pmode')).value = spec.runtimeHints.preferredMode || '';
        }

        /** @type {HTMLElement} */ (document.getElementById('pm-yaml-pre')).textContent = yamlPreview || '(save or load to refresh preview)';

        function showTab(which) {
            const formPane = document.getElementById('pm-pane-form');
            const yamlPane = document.getElementById('pm-pane-yaml');
            root.querySelectorAll('.pm-tab').forEach(function (b) {
                const on = b.getAttribute('data-t') === which;
                b.className =
                    'pm-tab px-4 py-2 text-sm font-medium rounded-t-lg ' +
                    (on
                        ? 'bg-blue-100 text-blue-800 dark:bg-blue-900 dark:text-blue-100'
                        : 'text-gray-600 hover:bg-gray-100 dark:text-gray-400 dark:hover:bg-gray-700');
            });
            if (which === 'yaml') {
                formPane.classList.add('hidden');
                yamlPane.classList.remove('hidden');
            } else {
                formPane.classList.remove('hidden');
                yamlPane.classList.add('hidden');
            }
        }

        root.querySelectorAll('.pm-tab').forEach(function (b) {
            b.addEventListener('click', function () {
                showTab(b.getAttribute('data-t') || 'form');
            });
        });

        document.getElementById('pm-save').addEventListener('click', function () {
            const msg = document.getElementById('pm-save-msg');
            const payload = readForm();
            if (!payload.spec.id) {
                msg.textContent = 'Id is required.';
                return;
            }
            msg.textContent = 'Saving…';
            fetch('/api/project-memory/agents/' + encodeURIComponent(payload.spec.id), {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ spec: payload.spec, relativePath: payload.relativePath })
            })
                .then(function (r) {
                    if (!r.ok) return r.text().then(function (t) {
                        throw new Error(t || String(r.status));
                    });
                    return r.json();
                })
                .then(function () {
                    msg.textContent = 'Saved.';
                    isExisting = true;
                    if (titleEl) titleEl.textContent = 'Agent: ' + payload.spec.id;
                    window.history.replaceState({}, '', '/Dashboard/ProjectMemory/Agents/Edit?id=' + encodeURIComponent(payload.spec.id));
                    if (window.AgctorPersonaToolsUi) window.AgctorPersonaToolsUi.invalidatePersona(payload.spec.id);
                    loadToolsPanel(payload.spec.id);
                    return fetch('/api/project-memory/agents/' + encodeURIComponent(payload.spec.id)).then(function (r) {
                        return r.ok ? r.json() : null;
                    });
                })
                .then(function (detail) {
                    if (detail && detail.yamlPreview) {
                        yamlPreview = detail.yamlPreview;
                        const pre = document.getElementById('pm-yaml-pre');
                        if (pre) pre.textContent = yamlPreview;
                    }
                })
                .catch(function (e) {
                    msg.textContent = 'Error: ' + (e.message || e);
                });
        });
    }

    function start() {
        if (!queryId) {
            isExisting = false;
            window.__pmSpec = normalizeSpec(defaultSpec());
            window.__pmRel = '';
            yamlPreview = '';
            if (titleEl) titleEl.textContent = 'New agent';
            renderForm();
            return;
        }

        isExisting = true;
        if (titleEl) titleEl.textContent = 'Agent: ' + queryId;
        fetch('/api/project-memory/agents/' + encodeURIComponent(queryId))
            .then(function (r) {
                if (r.status === 400)
                    return r.json().then(function (b) {
                        throw new Error(b.error || 'Configure project root');
                    });
                if (!r.ok) throw new Error(r.status === 404 ? 'Not found' : String(r.status));
                return r.json();
            })
            .then(function (d) {
                window.__pmSpec = normalizeSpec(d.spec);
                window.__pmRel = d.relativePath || '';
                yamlPreview = d.yamlPreview || '';
                renderForm();
            })
            .catch(function (e) {
                root.innerHTML =
                    '<div class="p-6 rounded-lg bg-red-50 border border-red-200 dark:bg-red-900/20"><p class="text-red-800 dark:text-red-200">' +
                    esc(e.message || 'Failed to load') +
                    '</p><p class="mt-3"><a href="/Dashboard/ProjectMemory/Maintenance" class="text-blue-600 hover:underline">Maintenance</a></p></div>';
            });
    }

    start();
})();
