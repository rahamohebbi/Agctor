/**
 * Lists project-memory agents; delete with confirm; links to editor.
 */
(function () {
    const root = document.getElementById('pm-agents-root');
    if (!root) return;

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    function load() {
        root.innerHTML =
            '<div class="flex items-center justify-center p-8 rounded-lg bg-gray-100 dark:bg-gray-800"><p class="text-gray-600 dark:text-gray-400">Loading…</p></div>';
        fetch('/api/project-memory/agents')
            .then(function (r) {
                if (r.status === 400)
                    return r.json().then(function (b) {
                        throw new Error(b.error || 'Bad request');
                    });
                return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
            })
            .then(function (list) {
                if (!Array.isArray(list) || list.length === 0) {
                    root.innerHTML =
                        '<div class="p-8 text-center rounded-lg border border-dashed border-gray-300 dark:border-gray-600">' +
                        '<p class="text-gray-600 dark:text-gray-400">No agents found. Create one from a template or add YAML under .agctor/agents/.</p>' +
                        '<p class="mt-4"><a href="/Dashboard/ProjectMemory/Templates" class="text-blue-600 hover:underline dark:text-blue-400">Browse templates</a></p></div>';
                    return;
                }
                let rows = '';
                for (let i = 0; i < list.length; i++) {
                    const a = list[i];
                    const id = esc(a.id);
                    rows +=
                        '<tr class="bg-white border-b dark:bg-gray-800 dark:border-gray-700">' +
                        '<td class="px-4 py-3 font-medium text-gray-900 dark:text-white">' +
                        id +
                        '</td>' +
                        '<td class="px-4 py-3">' +
                        esc(a.name || '') +
                        '</td>' +
                        '<td class="px-4 py-3 text-gray-600 dark:text-gray-300">' +
                        esc(a.role || '') +
                        '</td>' +
                        '<td class="px-4 py-3 text-xs font-mono text-gray-500 break-all max-w-xs">' +
                        esc(a.relativePath || '') +
                        '</td>' +
                        '<td class="px-4 py-3 whitespace-nowrap">' +
                        '<a href="/Dashboard/ProjectMemory/Agents/Edit?id=' +
                        encodeURIComponent(a.id) +
                        '" class="text-blue-600 hover:underline dark:text-blue-400 mr-3">Edit</a>' +
                        '<a href="/Dashboard/ProjectMemory/Playground?agentId=' +
                        encodeURIComponent(a.id) +
                        '" class="text-emerald-600 hover:underline dark:text-emerald-400 mr-3">Test</a>' +
                        '<button type="button" class="text-red-600 hover:underline dark:text-red-400 pm-del" data-id="' +
                        id +
                        '">Delete</button>' +
                        '</td></tr>';
                }
                root.innerHTML =
                    '<div class="overflow-x-auto rounded-lg border border-gray-200 dark:border-gray-700">' +
                    '<table class="w-full text-sm text-left text-gray-500 dark:text-gray-400">' +
                    '<thead class="text-xs text-gray-700 uppercase bg-gray-50 dark:bg-gray-700 dark:text-gray-300">' +
                    '<tr><th class="px-4 py-3">Id</th><th class="px-4 py-3">Name</th><th class="px-4 py-3">Role</th><th class="px-4 py-3">Path</th><th class="px-4 py-3">Actions</th></tr></thead>' +
                    '<tbody>' +
                    rows +
                    '</tbody></table></div>';

                root.querySelectorAll('.pm-del').forEach(function (btn) {
                    btn.addEventListener('click', function () {
                        const id = btn.getAttribute('data-id');
                        if (!id || !window.confirm('Delete agent "' + id + '" and its YAML file?')) return;
                        fetch('/api/project-memory/agents/' + encodeURIComponent(id), { method: 'DELETE' })
                            .then(function (r) {
                                if (r.ok) load();
                                else return r.text().then(function (t) {
                                    throw new Error(t || String(r.status));
                                });
                            })
                            .catch(function (e) {
                                window.alert('Delete failed: ' + (e.message || e));
                            });
                    });
                });
            })
            .catch(function (e) {
                root.innerHTML =
                    '<div class="p-6 rounded-lg bg-amber-50 border border-amber-200 dark:bg-amber-900/20 dark:border-amber-800">' +
                    '<p class="text-amber-900 dark:text-amber-100 font-medium">Agents unavailable</p>' +
                    '<p class="mt-2 text-sm text-amber-800 dark:text-amber-200">' +
                    esc(e.message || 'Set project root on Maintenance.') +
                    '</p>' +
                    '<p class="mt-3"><a href="/Dashboard/ProjectMemory/Maintenance" class="text-blue-600 hover:underline">Maintenance →</a></p></div>';
            });
    }

    load();
})();
