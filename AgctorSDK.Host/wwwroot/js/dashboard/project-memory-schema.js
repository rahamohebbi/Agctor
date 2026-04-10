/**
 * Schema Studio: tabbed YAML editors per segment; PUT to api/project-memory/schema/{segment}.
 */
(function () {
    const root = document.getElementById('pm-schema-root');
    if (!root) return;

    const segments = [
        { key: 'project-type', label: 'Project type' },
        { key: 'entity-types', label: 'Entity types' },
        { key: 'document-types', label: 'Document types' },
        { key: 'routing-rules', label: 'Routing rules' },
        { key: 'workspace', label: 'Workspace' }
    ];

    var files = {};
    var active = 'project-type';

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    function render() {
        let tabs = '';
        for (let i = 0; i < segments.length; i++) {
            const seg = segments[i];
            const on = seg.key === active;
            tabs +=
                '<button type="button" class="pm-seg px-3 py-2 text-sm font-medium rounded-lg ' +
                (on ? 'bg-blue-600 text-white' : 'bg-gray-100 text-gray-700 hover:bg-gray-200 dark:bg-gray-700 dark:text-gray-200') +
                '" data-seg="' +
                esc(seg.key) +
                '">' +
                esc(seg.label) +
                '</button>';
        }
        const f = files[active];
        const yaml = f && f.yaml ? f.yaml : '';
        const rel = f && f.relativePath ? f.relativePath : '';

        root.innerHTML =
            '<div class="flex flex-wrap gap-2 mb-4">' +
            tabs +
            '</div>' +
            '<p class="text-xs font-mono text-gray-500 mb-2 break-all">' +
            esc(rel) +
            '</p>' +
            '<textarea id="pm-schema-yaml" rows="24" class="w-full rounded-lg border border-gray-300 bg-gray-900 text-green-100 p-3 text-xs font-mono dark:border-gray-600"></textarea>' +
            '<div class="mt-3 flex gap-2 items-center">' +
            '<button type="button" id="pm-schema-save" class="px-4 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700">Save segment</button>' +
            '<span id="pm-schema-msg" class="text-sm text-gray-600 dark:text-gray-400"></span></div>';

        /** @type {HTMLTextAreaElement} */ (document.getElementById('pm-schema-yaml')).value = yaml;

        root.querySelectorAll('.pm-seg').forEach(function (b) {
            b.addEventListener('click', function () {
                const ta = /** @type {HTMLTextAreaElement} */ (document.getElementById('pm-schema-yaml'));
                if (ta && files[active]) files[active].yaml = ta.value;
                active = b.getAttribute('data-seg') || active;
                render();
            });
        });

        document.getElementById('pm-schema-save').addEventListener('click', function () {
            const ta = /** @type {HTMLTextAreaElement} */ (document.getElementById('pm-schema-yaml'));
            const body = ta ? ta.value : '';
            const msg = document.getElementById('pm-schema-msg');
            msg.textContent = 'Saving…';
            fetch('/api/project-memory/schema/' + encodeURIComponent(active), {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ yaml: body })
            })
                .then(function (r) {
                    if (!r.ok) return r.text().then(function (t) {
                        throw new Error(t || String(r.status));
                    });
                    files[active].yaml = body;
                    msg.textContent = 'Saved.';
                })
                .catch(function (e) {
                    msg.textContent = esc(e.message || String(e));
                });
        });
    }

    fetch('/api/project-memory/schema')
        .then(function (r) {
            if (r.status === 400)
                return r.json().then(function (b) {
                    throw new Error(b.error || b.title || 'Bad request');
                });
            return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
        })
        .then(function (bundle) {
            const raw = bundle.files || {};
            for (let i = 0; i < segments.length; i++) {
                const k = segments[i].key;
                files[k] = raw[k] ? { yaml: raw[k].yaml || '', relativePath: raw[k].relativePath || '' } : { yaml: '', relativePath: '' };
            }
            render();
        })
        .catch(function (e) {
            root.innerHTML =
                '<div class="p-6 rounded-lg bg-amber-50 border border-amber-200 dark:bg-amber-900/20">' +
                '<p class="text-amber-900 dark:text-amber-100">' +
                esc(e.message || 'Could not load schema') +
                '</p>' +
                '<p class="mt-2 text-sm"><a href="/Dashboard/ProjectMemory/Maintenance" class="text-blue-600 hover:underline">Set project root</a></p></div>';
        });
})();
