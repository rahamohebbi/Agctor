/**
 * Project memory dashboard landing: status card + shortcuts to Agents, Schema, Workspace, Maintenance.
 */
(function () {
    const root = document.getElementById('pm-index-root');
    if (!root) return;

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    function card(href, title, desc) {
        return (
            '<a href="' +
            esc(href) +
            '" class="block p-5 bg-white border border-gray-200 rounded-lg shadow hover:bg-gray-50 dark:bg-gray-800 dark:border-gray-700 dark:hover:bg-gray-700">' +
            '<h2 class="text-lg font-semibold text-gray-900 dark:text-white">' +
            esc(title) +
            '</h2>' +
            '<p class="mt-2 text-sm text-gray-500 dark:text-gray-400">' +
            esc(desc) +
            '</p></a>'
        );
    }

    fetch('/api/project-memory/status')
        .then(function (r) {
            return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
        })
        .then(function (st) {
            const hasRoot = st.projectRoot && String(st.projectRoot).length > 0;
            const ok = st.projectLoaded && !st.error;
            const statusClass = ok
                ? 'border-green-200 bg-green-50 dark:bg-green-900/20 dark:border-green-800'
                : 'border-amber-200 bg-amber-50 dark:bg-amber-900/20 dark:border-amber-800';
            const headline = ok
                ? 'Project loaded'
                : hasRoot
                  ? 'Project folder set — load issue'
                  : 'No project root';
            const detail = st.error
                ? st.error
                : ok
                  ? (st.projectId || '') +
                    (st.projectType ? ' · type: ' + st.projectType : '') +
                    (st.runtimeMode ? ' · ' + st.runtimeMode : '') +
                    ' · ' +
                    st.agentCount +
                    ' agent(s)'
                  : hasRoot
                    ? 'Check paths and YAML under .agctor.'
                    : 'Configure a folder with .agctor on the Maintenance page.';

            root.innerHTML =
                '<div class="p-6 rounded-lg border ' +
                statusClass +
                '">' +
                '<h2 class="text-lg font-semibold text-gray-900 dark:text-white">' +
                esc(headline) +
                '</h2>' +
                '<p class="mt-2 text-sm font-mono text-gray-800 dark:text-gray-200 break-all">' +
                esc(hasRoot ? st.projectRoot : '(not set)') +
                '</p>' +
                '<p class="mt-2 text-sm text-gray-600 dark:text-gray-300">' +
                esc(detail) +
                '</p>' +
                '<div class="mt-4 flex flex-wrap gap-2">' +
                '<a href="/Dashboard/ProjectMemory/Maintenance" class="inline-flex px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-gray-300 dark:bg-gray-800 dark:border-gray-600 dark:text-white">Open maintenance</a>' +
                '</div></div>' +
                '<div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">' +
                card('/Dashboard/Agents', 'Agents (unified)', 'C# types, YAML definitions, and scenario apply — primary entry (PRD-013).') +
                card('/Dashboard/ProjectMemory/Agents', 'Agents (advanced)', 'Full YAML editor, templates, playground — legacy studio path.') +
                card('/Dashboard/ProjectMemory/Templates', 'Templates', 'Create agents from built-in templates.') +
                card('/Dashboard/ProjectMemory/Playground', 'Playground', 'Run extraction/query tests against a selected agent spec.') +
                card('/Dashboard/ProjectMemory/Pipeline', 'Pipeline', 'Run extract → write → query with step timeline.') +
                card('/Dashboard/ProjectMemory/Projects', 'Projects', 'Create project buckets and move sessions in/out.') +
                card('/Dashboard/ProjectMemory/Schema', 'Schema studio', 'Project type, entities, documents, routing, workspace YAML.') +
                card('/Dashboard/ProjectMemory/Workspace', 'Workspace', 'Browse files and preview content.') +
                card('/Dashboard/ProjectMemory/Maintenance', 'Maintenance', 'Set project root, validate, rebuild.') +
                '</div>';
        })
        .catch(function () {
            root.innerHTML =
                '<div class="p-6 rounded-lg bg-red-50 border border-red-200 dark:bg-red-900/20 dark:border-red-800"><p class="text-red-800 dark:text-red-200">Could not load project memory status.</p></div>';
        });
})();
