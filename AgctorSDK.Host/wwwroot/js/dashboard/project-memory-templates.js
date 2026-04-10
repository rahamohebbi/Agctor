/**
 * Template gallery + short wizard: pick template → id + agents folder → create via API → open editor.
 */
(function () {
    const root = document.getElementById('pm-templates-root');
    if (!root) return;

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    var templates = [];
    var selected = null;
    var step = 1;
    /** Draft id + subfolder between wizard steps */
    var draftId = '';
    var draftSub = 'people';

    function render() {
        if (step === 1) {
            let cards = '';
            for (let i = 0; i < templates.length; i++) {
                const t = templates[i];
                cards +=
                    '<button type="button" class="text-left w-full p-5 bg-white border border-gray-200 rounded-lg shadow hover:bg-gray-50 dark:bg-gray-800 dark:border-gray-700 dark:hover:bg-gray-700 pm-pick" data-idx="' +
                    i +
                    '">' +
                    '<h3 class="text-lg font-semibold text-gray-900 dark:text-white">' +
                    esc(t.name || t.templateId) +
                    '</h3>' +
                    '<p class="mt-2 text-sm text-gray-500 dark:text-gray-400">' +
                    esc(t.description || '') +
                    '</p>' +
                    '<p class="mt-2 text-xs font-mono text-gray-400">' +
                    esc(t.templateId) +
                    '</p></button>';
            }
            root.innerHTML =
                '<div class="mb-4"><p class="text-sm text-gray-600 dark:text-gray-300">Step 1 of 3 — Choose a template</p></div>' +
                '<div class="grid gap-4 md:grid-cols-2 lg:grid-cols-3">' +
                cards +
                '</div>';
            root.querySelectorAll('.pm-pick').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    const idx = parseInt(btn.getAttribute('data-idx') || '0', 10);
                    selected = templates[idx];
                    step = 2;
                    render();
                });
            });
            return;
        }

        if (step === 2) {
            root.innerHTML =
                '<div class="mb-4 flex items-center gap-2">' +
                '<button type="button" class="text-sm text-blue-600 hover:underline pm-back">← Back</button>' +
                '<span class="text-sm text-gray-600 dark:text-gray-300">Step 2 of 3 — New agent id</span></div>' +
                '<div class="max-w-lg p-6 bg-white border border-gray-200 rounded-lg dark:bg-gray-800 dark:border-gray-700">' +
                '<p class="text-sm text-gray-600 dark:text-gray-400 mb-4">Template: <strong>' +
                esc(selected ? selected.name : '') +
                '</strong></p>' +
                '<div class="space-y-4">' +
                '<div><label class="block text-xs font-medium mb-1">Agent id (slug)</label>' +
                '<input id="pm-wiz-id" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" placeholder="e.g. my-extractor" /></div>' +
                '<div><label class="block text-xs font-medium mb-1">Agents subfolder under .agctor/agents/</label>' +
                '<input id="pm-wiz-sub" type="text" class="w-full rounded-lg border border-gray-300 bg-gray-50 p-2.5 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white" /></div>' +
                '</div>' +
                '<button type="button" id="pm-wiz-next" class="mt-6 px-4 py-2.5 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700">Review</button></div>';

            /** @type {HTMLInputElement} */ (document.getElementById('pm-wiz-id')).value = draftId;
            /** @type {HTMLInputElement} */ (document.getElementById('pm-wiz-sub')).value = draftSub;

            document.querySelector('.pm-back').addEventListener('click', function () {
                step = 1;
                render();
            });
            document.getElementById('pm-wiz-next').addEventListener('click', function () {
                const idEl = /** @type {HTMLInputElement} */ (document.getElementById('pm-wiz-id'));
                const subEl = /** @type {HTMLInputElement} */ (document.getElementById('pm-wiz-sub'));
                draftId = idEl ? idEl.value.trim() : '';
                draftSub = subEl && subEl.value.trim() ? subEl.value.trim() : 'people';
                if (!draftId) {
                    window.alert('Enter an agent id.');
                    return;
                }
                step = 3;
                render();
            });
            return;
        }

        if (step === 3 && selected) {
            const newId = draftId;
            const agentsSubfolder = draftSub;

            root.innerHTML =
                '<div class="mb-4 flex items-center gap-2">' +
                '<button type="button" class="text-sm text-blue-600 hover:underline pm-back2">← Back</button>' +
                '<span class="text-sm text-gray-600 dark:text-gray-300">Step 3 of 3 — Confirm</span></div>' +
                '<div class="max-w-lg p-6 bg-white border border-gray-200 rounded-lg dark:bg-gray-800 dark:border-gray-700">' +
                '<dl class="text-sm space-y-2">' +
                '<div><dt class="text-gray-500">Template</dt><dd class="font-mono">' +
                esc(selected.templateId) +
                '</dd></div>' +
                '<div><dt class="text-gray-500">New id</dt><dd class="font-mono">' +
                esc(newId) +
                '</dd></div>' +
                '<div><dt class="text-gray-500">Folder</dt><dd class="font-mono">.agctor/agents/' +
                esc(agentsSubfolder) +
                '/' +
                esc(newId) +
                '.agent.yaml</dd></div></dl>' +
                '<button type="button" id="pm-wiz-create" class="mt-6 px-4 py-2.5 text-sm font-medium text-white bg-emerald-600 rounded-lg hover:bg-emerald-700">Create agent</button>' +
                '<p id="pm-wiz-err" class="mt-2 text-sm text-red-600"></p></div>';

            document.querySelector('.pm-back2').addEventListener('click', function () {
                step = 2;
                render();
            });

            document.getElementById('pm-wiz-create').addEventListener('click', function () {
                const err = document.getElementById('pm-wiz-err');
                if (!newId) {
                    err.textContent = 'Enter an agent id.';
                    return;
                }
                err.textContent = 'Creating…';
                fetch('/api/project-memory/agents/from-template', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        templateId: selected.templateId,
                        newId: newId,
                        agentsSubfolder: agentsSubfolder
                    })
                })
                    .then(function (r) {
                        if (!r.ok) return r.text().then(function (t) {
                            throw new Error(t || String(r.status));
                        });
                        return r.json();
                    })
                    .then(function () {
                        window.location.href = '/Dashboard/ProjectMemory/Agents/Edit?id=' + encodeURIComponent(newId);
                    })
                    .catch(function (e) {
                        err.textContent = e.message || String(e);
                    });
            });
        }
    }

    fetch('/api/project-memory/templates')
        .then(function (r) {
            return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
        })
        .then(function (list) {
            templates = Array.isArray(list) ? list : [];
            if (templates.length === 0) {
                root.innerHTML =
                    '<p class="text-gray-600 dark:text-gray-400">No templates found (check wwwroot/templates/project-memory/agent-templates.json).</p>';
                return;
            }
            render();
        })
        .catch(function () {
            root.innerHTML =
                '<p class="text-red-600 dark:text-red-400">Could not load templates. Is the project root configured?</p>';
        });
})();
