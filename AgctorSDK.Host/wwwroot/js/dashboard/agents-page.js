/**
 * Dashboard Agents page (PRD-010): unified agent-type list, Flowbite-styled toggles,
 * single configured scenario Apply, and refresh.
 */
(function () {
    const el = document.getElementById('agents-content');
    if (!el) return;

    let configData = null;
    let agentsData = [];
    let currentScenario = null;
    let lastLoadedAt = null;
    const backendErrors = new Map();
    let refreshTimer = null;
    /** Prevents PUT loop when we refresh after toggle */
    let suppressToggleUntil = 0;

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

    function refreshMetaText(list) {
        const count = Array.isArray(list) ? list.length : 0;
        const loaded = lastLoadedAt ? new Date(lastLoadedAt).toLocaleTimeString() : 'Not loaded yet';
        return esc(String(count)) + ' active instance(s). Last refreshed at ' + esc(loaded) + '.';
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

    function render(config, agents, current) {
        const list = Array.isArray(agents) ? agents : [];
        const agentTypes = config.agentTypes && typeof config.agentTypes === 'object' ? config.agentTypes : {};
        const enablement = config.agentTypeEnablement && typeof config.agentTypeEnablement === 'object'
            ? config.agentTypeEnablement
            : {};
        const scenarioName = config.dashboardScenarioName || '';
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

        el.innerHTML =
            errorBlock +
            currentBlock +
            '<div class="p-6 bg-white border border-gray-200 rounded-lg shadow dark:bg-gray-800 dark:border-gray-700 mb-6">' +
            '<div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">' +
            '<div>' +
            '<h2 class="text-lg font-semibold text-gray-900 dark:text-white">Scenario</h2>' +
            '<p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Dashboard uses one configured scenario: <strong class="text-gray-800 dark:text-gray-200">' +
            esc(scenarioName) +
            '</strong> (set <code class="text-xs bg-gray-100 dark:bg-gray-700 px-1 rounded">Agctor:Dashboard:ScenarioName</code>).</p>' +
            '</div>' +
            '<div class="flex flex-wrap gap-2">' +
            '<button type="button" data-apply-scenario class="text-white bg-blue-700 hover:bg-blue-800 focus:ring-4 focus:ring-blue-300 font-medium rounded-lg text-sm px-5 py-2.5 dark:bg-blue-600 dark:hover:bg-blue-700 focus:outline-none dark:focus:ring-blue-800">' +
            'Apply scenario' +
            '</button>' +
            '<button type="button" data-refresh-agents class="py-2.5 px-5 text-sm font-medium text-gray-900 focus:outline-none bg-white rounded-lg border border-gray-200 hover:bg-gray-100 hover:text-blue-700 focus:z-10 focus:ring-4 focus:ring-gray-100 dark:focus:ring-gray-700 dark:bg-gray-800 dark:text-gray-400 dark:border-gray-600 dark:hover:text-white dark:hover:bg-gray-700">' +
            'Refresh' +
            '</button>' +
            '</div></div>' +
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

    async function refreshRuntimeData(showInlineError) {
        const [agents, current] = await Promise.all([
            fetchJson('/api/agents', 'agents-api', agentsData),
            fetchJson('/api/Test/current-scenario', 'current-scenario-api', currentScenario)
        ]);

        agentsData = Array.isArray(agents) ? agents : [];
        currentScenario = current;
        lastLoadedAt = Date.now();

        if (configData) {
            render(configData, agentsData, currentScenario);
        }

        if (showInlineError && backendErrors.size > 0) {
            showScenarioMessage('One or more backend issues were detected. See the red panel above.', true);
        }
    }

    async function applyScenario() {
        showScenarioMessage('Applying...', false);
        try {
            const res = await fetch('/api/Test/setup-scenario', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ parameters: {} })
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok || data.success === false) {
                const msg = data.errorMessage || data.message || 'Failed to apply scenario.';
                setBackendError('scenario-apply', msg);
                await refreshRuntimeData(false);
                showScenarioMessage(msg, true);
                return;
            }
            clearBackendError('scenario-apply');
            const count = data.createdAgentIds ? data.createdAgentIds.length : 0;
            const successMsg = 'Scenario applied. ' + count + ' agent(s) reported created.';
            await refreshRuntimeData(false);
            showScenarioMessage(successMsg, false);
        } catch (e) {
            const msg = 'Error: ' + (e.message || 'Request failed.');
            setBackendError('scenario-apply', msg);
            await refreshRuntimeData(false);
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
        if (e.target.matches('button[data-apply-scenario]')) applyScenario();
        if (e.target.matches('button[data-refresh-agents]')) refreshRuntimeData(true);
    });

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
            await refreshRuntimeData(false);
        } catch {
            input.checked = !enabled;
            await refreshRuntimeData(true);
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
            await refreshRuntimeData(false);

            if (refreshTimer) window.clearInterval(refreshTimer);
            refreshTimer = window.setInterval(function () {
                refreshRuntimeData(false);
            }, 5000);
        })
        .catch(() => {
            el.innerHTML =
                '<div class="p-6 bg-red-50 border border-red-200 rounded-lg dark:bg-gray-800 dark:border-red-800"><p class="text-red-700 dark:text-red-400">Failed to load agents.</p></div>';
        });
})();
