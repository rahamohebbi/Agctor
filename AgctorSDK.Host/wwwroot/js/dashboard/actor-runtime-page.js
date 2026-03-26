/**
 * Actor runtime dashboard (PRD-012): GET /api/runtime, catalog cards, Tier A PUT + restart banner.
 */
(function () {
    const el = document.getElementById('actor-runtime-content');
    if (!el) return;

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    function fmtNum(n) {
        if (n == null || Number.isNaN(n)) return '—';
        return String(n);
    }

    function renderStats(s) {
        if (!s) return '<p class="text-sm text-gray-500 dark:text-gray-400">Statistics unavailable for this runtime.</p>';
        return (
            '<dl class="mt-3 grid grid-cols-2 gap-2 text-sm sm:grid-cols-3">' +
            '<div><dt class="text-gray-500 dark:text-gray-400">Active actors</dt><dd class="font-medium text-gray-900 dark:text-white">' +
            esc(fmtNum(s.activeActorCount)) +
            '</dd></div>' +
            '<div><dt class="text-gray-500 dark:text-gray-400">Messages (total)</dt><dd class="font-medium text-gray-900 dark:text-white">' +
            esc(fmtNum(s.totalMessagesProcessed)) +
            '</dd></div>' +
            '<div><dt class="text-gray-500 dark:text-gray-400">Uptime (s)</dt><dd class="font-medium text-gray-900 dark:text-white">' +
            esc(fmtNum(s.uptimeSeconds != null ? s.uptimeSeconds.toFixed(0) : null)) +
            '</dd></div>' +
            '</dl>'
        );
    }

    function renderCapabilityTags(caps) {
        const list = Array.isArray(caps) ? caps : [];
        if (!list.length) return '';
        return (
            '<div class="flex flex-wrap gap-1 mt-2">' +
            list
                .map(
                    (c) =>
                        '<span class="text-xs font-medium px-2 py-0.5 rounded bg-blue-100 text-blue-800 dark:bg-blue-900/40 dark:text-blue-200">' +
                        esc(c) +
                        '</span>'
                )
                .join('') +
            '</div>'
        );
    }

    function render(data, bannerHtml) {
        const cur = data.current || {};
        const cfg = data.configured || {};
        const available = Array.isArray(data.available) ? data.available : [];
        const mismatch =
            cur.canonicalId &&
            cfg.defaultRuntime &&
            String(cur.canonicalId).toLowerCase() !== String(cfg.defaultRuntime).toLowerCase();

        let cards = '';
        for (const a of available) {
            const caps = renderCapabilityTags(a.capabilities);
            const warn =
                a.hasCatalogEntry === false
                    ? '<p class="text-xs text-amber-700 dark:text-amber-300 mt-1">No catalog copy; id from factory only.</p>'
                    : '';
            cards +=
                '<div class="p-4 rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 shadow-sm">' +
                '<h3 class="font-semibold text-gray-900 dark:text-white">' +
                esc(a.displayName || a.id) +
                '</h3>' +
                '<p class="text-xs text-gray-500 dark:text-gray-400 mt-0.5 font-mono">' +
                esc(a.id) +
                '</p>' +
                (a.summary ? '<p class="text-sm text-gray-600 dark:text-gray-300 mt-2">' + esc(a.summary) + '</p>' : '') +
                (a.limitations
                    ? '<p class="text-xs text-gray-500 dark:text-gray-400 mt-2"><span class="font-medium">Limits:</span> ' +
                      esc(a.limitations) +
                      '</p>'
                    : '') +
                (a.deploymentNotes
                    ? '<p class="text-xs text-gray-500 dark:text-gray-400 mt-1"><span class="font-medium">Deploy:</span> ' +
                      esc(a.deploymentNotes) +
                      '</p>'
                    : '') +
                caps +
                warn +
                '</div>';
        }

        const protoVal = cfg.protoPort != null ? String(cfg.protoPort) : '';
        const protoHostVal = cfg.protoHost != null ? String(cfg.protoHost) : '';

        el.innerHTML =
            (bannerHtml || '') +
            (mismatch
                ? '<div class="p-4 mb-4 rounded-lg bg-amber-50 border border-amber-200 dark:bg-amber-900/20 dark:border-amber-800" role="status">' +
                  '<p class="text-sm font-medium text-amber-900 dark:text-amber-100">Live vs next boot</p>' +
                  '<p class="text-sm text-amber-800 dark:text-amber-200 mt-1">Running <strong>' +
                  esc(cur.canonicalId) +
                  '</strong> but configuration requests <strong>' +
                  esc(cfg.defaultRuntime) +
                  '</strong> after restart.</p></div>'
                : '') +
            '<div class="p-6 rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800">' +
            '<h2 class="text-lg font-semibold text-gray-900 dark:text-white">Current process</h2>' +
            '<dl class="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2 text-sm">' +
            '<div><dt class="text-gray-500 dark:text-gray-400">Canonical id</dt><dd class="font-mono text-gray-900 dark:text-white">' +
            esc(cur.canonicalId) +
            '</dd></div>' +
            '<div><dt class="text-gray-500 dark:text-gray-400">Adapter name</dt><dd class="font-mono text-gray-900 dark:text-white">' +
            esc(cur.adapterName) +
            '</dd></div>' +
            '<div><dt class="text-gray-500 dark:text-gray-400">Version</dt><dd class="text-gray-900 dark:text-white">' +
            esc(cur.version) +
            '</dd></div>' +
            '<div><dt class="text-gray-500 dark:text-gray-400">Initialized</dt><dd class="text-gray-900 dark:text-white">' +
            esc(cur.isInitialized ? 'Yes' : 'No') +
            '</dd></div>' +
            '</dl>' +
            renderStats(cur.statistics) +
            '</div>' +
            '<div class="p-6 rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800">' +
            '<h2 class="text-lg font-semibold text-gray-900 dark:text-white">Save for next boot</h2>' +
            '<p class="text-sm text-gray-500 dark:text-gray-400 mt-1">Writes <code class="text-xs bg-gray-100 dark:bg-gray-700 px-1 rounded">Agctor:DefaultRuntime</code> and optional Proto settings. Restart required.</p>' +
            '<form id="runtime-save-form" class="mt-4 space-y-4 max-w-xl">' +
            '<div>' +
            '<label for="rt-default" class="block text-sm font-medium text-gray-700 dark:text-gray-300">Default runtime</label>' +
            '<select id="rt-default" name="defaultRuntime" class="mt-1 block w-full rounded-md border-gray-300 dark:border-gray-600 dark:bg-gray-700 dark:text-white shadow-sm focus:border-blue-500 focus:ring-blue-500 text-sm">' +
            available
                .map(
                    (a) =>
                        '<option value="' +
                        esc(a.id) +
                        '"' +
                        (String(cfg.defaultRuntime || '').toLowerCase() === String(a.id).toLowerCase() ? ' selected' : '') +
                        '>' +
                        esc(a.displayName || a.id) +
                        '</option>'
                )
                .join('') +
            '</select></div>' +
            '<div>' +
            '<label for="rt-proto-host" class="block text-sm font-medium text-gray-700 dark:text-gray-300">Proto host (optional)</label>' +
            '<input type="text" id="rt-proto-host" name="protoHost" value="' +
            esc(protoHostVal) +
            '" placeholder="127.0.0.1" class="mt-1 block w-full rounded-md border-gray-300 dark:border-gray-600 dark:bg-gray-700 dark:text-white shadow-sm text-sm" />' +
            '</div>' +
            '<div>' +
            '<label for="rt-proto-port" class="block text-sm font-medium text-gray-700 dark:text-gray-300">Proto port (optional)</label>' +
            '<input type="number" id="rt-proto-port" name="protoPort" min="1" max="65535" value="' +
            esc(protoVal) +
            '" placeholder="12000" class="mt-1 block w-full rounded-md border-gray-300 dark:border-gray-600 dark:bg-gray-700 dark:text-white shadow-sm text-sm" />' +
            '</div>' +
            '<button type="submit" class="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500">Save</button>' +
            '</form></div>' +
            '<div><h2 class="text-lg font-semibold text-gray-900 dark:text-white mb-3">Runtime catalog</h2>' +
            '<div class="grid gap-4 md:grid-cols-2 lg:grid-cols-3">' +
            cards +
            '</div></div>';
    }

    async function attachSaveHandler() {
        const form = document.getElementById('runtime-save-form');
        if (!form) return;
        form.addEventListener('submit', async (ev) => {
            ev.preventDefault();
            const fd = new FormData(form);
            const defaultRuntime = fd.get('defaultRuntime');
            const protoHostRaw = fd.get('protoHost');
            const protoPortRaw = fd.get('protoPort');
            const body = { defaultRuntime: String(defaultRuntime || '') };
            const ph = protoHostRaw != null ? String(protoHostRaw).trim() : '';
            if (ph) body.protoHost = ph;
            if (protoPortRaw != null && String(protoPortRaw).trim() !== '') {
                const p = parseInt(String(protoPortRaw), 10);
                if (!Number.isNaN(p)) body.protoPort = p;
            }
            const putRes = await fetch('/api/runtime', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body),
            });
            const payload = await putRes.json().catch(() => ({}));
            if (!putRes.ok) {
                const msg = payload.message || payload.Message || putRes.statusText;
                alert('Save failed: ' + msg);
                return;
            }
            const message = payload.message || payload.Message || 'Saved.';
            const refreshed = await fetch('/api/runtime').then((r) => r.json());
            const banner =
                '<div class="p-4 mb-4 rounded-lg bg-green-50 border border-green-200 dark:bg-green-900/20 dark:border-green-800">' +
                '<p class="text-sm font-medium text-green-900 dark:text-green-100">' +
                esc(message) +
                '</p>' +
                '<p class="text-sm text-green-800 dark:text-green-200 mt-2"><strong>Restart the Host</strong> to load the new actor runtime.</p></div>';
            render(refreshed, banner);
            await attachSaveHandler();
        });
    }

    async function load() {
        const res = await fetch('/api/runtime');
        if (!res.ok) {
            el.innerHTML =
                '<div class="p-4 rounded-lg bg-red-50 border border-red-200 dark:bg-red-900/20 dark:border-red-800 text-red-800 dark:text-red-200 text-sm">Failed to load runtime (' +
                res.status +
                ').</div>';
            return;
        }
        const data = await res.json();
        render(data, '');
        await attachSaveHandler();
    }

    load();
})();
