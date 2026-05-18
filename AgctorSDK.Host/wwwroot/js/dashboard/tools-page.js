/**
 * Dashboard Tools page: GET /api/tools/agent-associations — tool list + per-tool agent associations (dynamic).
 */
(function () {
    const root = document.getElementById('tools-dashboard-root');
    if (!root) return;

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    let payload = null;
    let selectedClr = null;

    function render() {
        if (!payload || !Array.isArray(payload.tools)) {
            root.innerHTML =
                '<div class="p-4 rounded-lg bg-red-50 border border-red-200 text-red-800 dark:bg-red-900/20 dark:border-red-800 dark:text-red-200">Could not load tool associations.</div>';
            return;
        }

        const tools = payload.tools;
        if (!tools.length) {
            root.innerHTML =
                '<p class="text-sm text-gray-600 dark:text-gray-400">No registered tools found on this host.</p>';
            return;
        }

        if (!selectedClr || !tools.some(function (t) { return t.clrTypeName === selectedClr; }))
            selectedClr = tools[0].clrTypeName;

        const cur = tools.find(function (t) {
            return t.clrTypeName === selectedClr;
        });
        const assocs = (cur && cur.associations) || [];

        const left = tools
            .map(function (t) {
                const active = t.clrTypeName === selectedClr;
                const badge = t.httpPrimaryId
                    ? '<span class="ml-1 text-[10px] px-1.5 py-0.5 rounded bg-gray-200 text-gray-700 dark:bg-gray-600 dark:text-gray-200">' +
                      esc(t.httpPrimaryId) +
                      '</span>'
                    : '';
                const regBadge =
                    t.isRegistered === false
                        ? '<span class="ml-1 text-[10px] px-1.5 py-0.5 rounded bg-amber-100 text-amber-900 dark:bg-amber-900/40 dark:text-amber-100">not registered</span>'
                        : '';
                return (
                    '<button type="button" data-tool-clr="' +
                    esc(t.clrTypeName) +
                    '" class="w-full text-left px-3 py-2 rounded-lg text-sm transition-colors ' +
                    (active
                        ? 'bg-blue-600 text-white shadow'
                        : 'bg-white border border-gray-200 text-gray-800 hover:bg-gray-50 dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100 dark:hover:bg-gray-700') +
                    '">' +
                    '<span class="font-medium">' +
                    esc(t.displayName) +
                    '</span>' +
                    '<span class="block text-xs opacity-80 mt-0.5">' +
                    esc(t.clrTypeName) +
                    badge +
                    regBadge +
                    '</span></button>'
                );
            })
            .join('');

        const rightRows = assocs.length
            ? assocs
                  .map(function (a) {
                      return (
                          '<tr class="border-b border-gray-100 dark:border-gray-700">' +
                          '<td class="py-2 pr-4 text-sm font-medium text-gray-900 dark:text-white">' +
                          esc(a.agentLabel) +
                          '</td>' +
                          '<td class="py-2 pr-4 text-xs text-gray-500 dark:text-gray-400">' +
                          esc(a.kind) +
                          '</td>' +
                          '<td class="py-2 pr-4 text-xs text-gray-600 dark:text-gray-300">' +
                          esc(a.source) +
                          '</td>' +
                          '<td class="py-2 text-xs text-gray-500 dark:text-gray-400 break-all">' +
                          esc(a.detail || '') +
                          '</td></tr>'
                      );
                  })
                  .join('')
            : '<tr><td colspan="4" class="py-6 text-sm text-gray-500 dark:text-gray-400">No linked agents for this tool.</td></tr>';

        const unmapped = Array.isArray(payload.unmappedYamlAllowTokens) ? payload.unmappedYamlAllowTokens : [];
        const unmappedBlock =
            unmapped.length === 0
                ? ''
                : '<div class="mt-8 p-4 rounded-lg border border-amber-200 bg-amber-50 dark:border-amber-900 dark:bg-amber-900/20">' +
                  '<h3 class="text-sm font-semibold text-amber-900 dark:text-amber-100">YAML allow tokens not mapped to a host tool</h3>' +
                  '<p class="mt-1 text-xs text-amber-800 dark:text-amber-200">These usually name project-memory operations (e.g. read_document), not CLR tool actors.</p>' +
                  '<div class="mt-3 overflow-x-auto"><table class="min-w-full text-sm">' +
                  '<thead><tr class="text-left text-xs text-amber-900 dark:text-amber-200">' +
                  '<th class="pb-2 pr-4">Agent</th><th class="pb-2">Token</th></tr></thead><tbody>' +
                  unmapped
                      .map(function (u) {
                          return (
                              '<tr class="border-t border-amber-200/60 dark:border-amber-800/40">' +
                              '<td class="py-1.5 pr-4">' +
                              esc(u.agentLabel) +
                              ' <span class="text-xs text-amber-700 dark:text-amber-300">(' +
                              esc(u.agentId) +
                              ')</span></td>' +
                              '<td class="py-1.5 font-mono text-xs">' +
                              esc(u.token) +
                              '</td></tr>'
                          );
                      })
                      .join('') +
                  '</tbody></table></div></div>';

        root.innerHTML =
            '<div class="flex flex-col sm:flex-row gap-4 items-start">' +
            '<div class="w-full sm:w-64 shrink-0 space-y-1">' +
            left +
            '</div>' +
            '<div class="flex-1 min-w-0 space-y-3">' +
            '<div class="flex flex-wrap items-center gap-2">' +
            '<h2 class="text-lg font-semibold text-gray-900 dark:text-white">' +
            esc(cur.displayName) +
            '</h2>' +
            '<code class="text-xs bg-gray-100 dark:bg-gray-800 px-2 py-0.5 rounded">' +
            esc(cur.clrTypeName) +
            '</code>' +
            (cur.httpPrimaryId
                ? '<span class="text-xs text-gray-500 dark:text-gray-400">REST: <code class="bg-gray-100 dark:bg-gray-800 px-1 rounded">' +
                  esc(cur.httpPrimaryId) +
                  '</code></span>'
                : '') +
            '</div>' +
            (cur.description
                ? '<p class="text-sm text-gray-600 dark:text-gray-300 leading-relaxed">' + esc(cur.description) + '</p>'
                : '') +
            '<div class="overflow-x-auto rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800">' +
            '<table class="min-w-full text-left">' +
            '<thead><tr class="text-xs uppercase tracking-wide text-gray-500 dark:text-gray-400 border-b border-gray-200 dark:border-gray-700">' +
            '<th class="px-4 py-2">Agent</th><th class="px-4 py-2">Kind</th><th class="px-4 py-2">Source</th><th class="px-4 py-2">Detail</th></tr></thead>' +
            '<tbody class="divide-y divide-gray-100 dark:divide-gray-700">' +
            rightRows +
            '</tbody></table></div>' +
            '<p class="text-xs text-gray-500 dark:text-gray-400">Generated at ' +
            esc(payload.generatedAt || '') +
            '. <button type="button" id="tools-refresh" class="text-blue-600 hover:underline dark:text-blue-400">Refresh</button></p>' +
            '</div></div>' +
            unmappedBlock;

        root.querySelectorAll('[data-tool-clr]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                selectedClr = btn.getAttribute('data-tool-clr');
                render();
            });
        });
        const refBtn = document.getElementById('tools-refresh');
        if (refBtn) refBtn.addEventListener('click', load);
    }

    function load() {
        root.innerHTML =
            '<div class="flex items-center justify-center p-8 rounded-lg bg-gray-100 dark:bg-gray-800"><p class="text-sm text-gray-600 dark:text-gray-400">Loading…</p></div>';
        fetch('/api/tools/agent-associations')
            .then(function (r) {
                return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
            })
            .then(function (data) {
                payload = data;
                render();
            })
            .catch(function () {
                root.innerHTML =
                    '<div class="p-4 rounded-lg bg-red-50 border border-red-200 text-red-800 dark:bg-red-900/20 dark:border-red-800 dark:text-red-200">Failed to load <code class="text-xs">/api/tools/agent-associations</code>.</div>';
            });
    }

    load();
})();
