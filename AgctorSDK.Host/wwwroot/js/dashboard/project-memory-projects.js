/**
 * Project buckets UI: create/list/delete projects and move sessions.
 */
(function () {
    var nameInput = document.getElementById('pm-proj-name');
    var scenarioSel = document.getElementById('pm-proj-scenario');
    var createBtn = document.getElementById('pm-proj-create');
    var refreshBtn = document.getElementById('pm-proj-refresh');
    var status = document.getElementById('pm-proj-status');
    var list = document.getElementById('pm-proj-list');
    var current = document.getElementById('pm-proj-current');
    var projectSessions = document.getElementById('pm-proj-sessions');
    var standalone = document.getElementById('pm-proj-standalone');
    if (!nameInput || !scenarioSel || !createBtn || !refreshBtn || !status || !list || !current || !projectSessions || !standalone) return;

    var activeProjectId = null;
    var projectsById = {};

    function esc(s) {
        var d = document.createElement('div');
        d.textContent = s == null ? '' : String(s);
        return d.innerHTML;
    }

    function api(url, opt) {
        return fetch(url, opt).then(function (r) {
            if (!r.ok) return r.text().then(function (t) { throw new Error(t || String(r.status)); });
            if (r.status === 204) return null;
            return r.json();
        });
    }

    function loadScenarios() {
        return api('/api/scenarios')
            .then(function (items) {
                scenarioSel.innerHTML = '';
                (items || []).forEach(function (s) {
                    var opt = document.createElement('option');
                    opt.value = s.id;
                    opt.textContent = (s.displayName || s.id) + ' [' + s.id + ']';
                    scenarioSel.appendChild(opt);
                });
                if (!scenarioSel.value && scenarioSel.options.length > 0) scenarioSel.value = 'people';
            });
    }

    function renderProjects(items) {
        projectsById = {};
        if (!items || !items.length) {
            list.innerHTML = '<div class="text-xs text-gray-500 dark:text-gray-400">No projects yet.</div>';
            return;
        }
        var html = '';
        for (var i = 0; i < items.length; i++) {
            var p = items[i];
            projectsById[p.projectId] = p;
            var active = activeProjectId === p.projectId;
            html += '<div class="rounded border p-2 ' + (active ? 'border-blue-300 dark:border-blue-700' : 'border-gray-200 dark:border-gray-700') + '">';
            html += '<div class="flex items-center justify-between gap-2">';
            html += '<button class="pm-proj-pick text-left min-w-0" data-id="' + esc(p.projectId) + '">';
            html += '<div class="font-medium text-gray-900 dark:text-white truncate">' + esc(p.name || p.projectId) + '</div>';
            html += '<div class="text-xs text-gray-500 dark:text-gray-400">' + esc(p.scenarioId || '') + ' · ' + esc(p.sessionCount || 0) + ' sessions</div>';
            html += '</button>';
            var pQ = encodeURIComponent(p.projectId);
            html += '<div class="flex items-center gap-1">';
            html += '<a class="px-2 py-1 text-xs font-medium rounded bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300" href="/Dashboard/ProjectMemory/Playground?projectId=' + pQ + '">Play</a>';
            html += '<a class="px-2 py-1 text-xs font-medium rounded bg-indigo-50 text-indigo-700 dark:bg-indigo-900/30 dark:text-indigo-300" href="/Dashboard/ProjectMemory/Pipeline?projectId=' + pQ + '">Pipe</a>';
            html += '<button class="pm-proj-del px-2 py-1 text-xs font-medium rounded bg-red-50 text-red-700 dark:bg-red-900/30 dark:text-red-300" data-id="' + esc(p.projectId) + '">Delete</button>';
            html += '</div>';
            html += '</div></div>';
        }
        list.innerHTML = html;

        list.querySelectorAll('.pm-proj-pick').forEach(function (btn) {
            btn.addEventListener('click', function () {
                activeProjectId = btn.getAttribute('data-id');
                refreshAll();
            });
        });
        list.querySelectorAll('.pm-proj-del').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var id = btn.getAttribute('data-id');
                if (!confirm('Delete project and detach its sessions?')) return;
                api('/api/chat/projects/' + encodeURIComponent(id), { method: 'DELETE' })
                    .then(function () {
                        if (activeProjectId === id) activeProjectId = null;
                        return refreshAll();
                    })
                    .catch(function (e) { status.textContent = e.message || 'Delete failed'; });
            });
        });
    }

    function renderProjectSessions(items) {
        if (!activeProjectId) {
            current.textContent = 'No project selected.';
            projectSessions.innerHTML = '';
            return;
        }
        var p = projectsById[activeProjectId];
        current.textContent = (p ? (p.name + ' [' + (p.scenarioId || '') + ']') : activeProjectId);
        if (!items || !items.length) {
            projectSessions.innerHTML = '<div class="text-xs text-gray-500 dark:text-gray-400">No sessions in this project.</div>';
            return;
        }
        var html = '';
        for (var i = 0; i < items.length; i++) {
            var s = items[i];
            html += '<div class="rounded border border-gray-200 dark:border-gray-700 p-2 flex items-center justify-between gap-2">';
            html += '<div class="min-w-0"><div class="font-medium text-gray-900 dark:text-white truncate">' + esc(s.title || s.sessionId) + '</div>';
            html += '<div class="text-xs text-gray-500 dark:text-gray-400">' + esc(s.sessionId) + ' · turns ' + esc(s.turnCount || 0) + '</div></div>';
            var sQ = encodeURIComponent(s.sessionId);
            var pQ2 = encodeURIComponent(activeProjectId);
            html += '<div class="flex items-center gap-1">';
            html += '<a class="px-2 py-1 text-xs font-medium rounded bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300" href="/Dashboard/ProjectMemory/Playground?projectId=' + pQ2 + '&sessionId=' + sQ + '">Play</a>';
            html += '<a class="px-2 py-1 text-xs font-medium rounded bg-indigo-50 text-indigo-700 dark:bg-indigo-900/30 dark:text-indigo-300" href="/Dashboard/ProjectMemory/Pipeline?projectId=' + pQ2 + '&sessionId=' + sQ + '">Pipe</a>';
            html += '<button class="pm-proj-detach px-2 py-1 text-xs font-medium rounded bg-gray-100 text-gray-800 dark:bg-gray-700 dark:text-gray-100" data-id="' + esc(s.sessionId) + '">Detach</button>';
            html += '</div>';
            html += '</div>';
        }
        projectSessions.innerHTML = html;
        projectSessions.querySelectorAll('.pm-proj-detach').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var sid = btn.getAttribute('data-id');
                api('/api/chat/sessions/' + encodeURIComponent(sid) + '/project', { method: 'DELETE' })
                    .then(refreshAll)
                    .catch(function (e) { status.textContent = e.message || 'Detach failed'; });
            });
        });
    }

    function renderStandaloneSessions(items) {
        if (!items || !items.length) {
            standalone.innerHTML = '<div class="text-xs text-gray-500 dark:text-gray-400">No standalone sessions.</div>';
            return;
        }
        var html = '';
        for (var i = 0; i < items.length; i++) {
            var s = items[i];
            html += '<div class="rounded border border-gray-200 dark:border-gray-700 p-2 flex items-center justify-between gap-2">';
            html += '<div class="min-w-0"><div class="font-medium text-gray-900 dark:text-white truncate">' + esc(s.title || s.sessionId) + '</div>';
            html += '<div class="text-xs text-gray-500 dark:text-gray-400">' + esc(s.sessionId) + ' · turns ' + esc(s.turnCount || 0) + '</div></div>';
            if (activeProjectId) {
                var sQ2 = encodeURIComponent(s.sessionId);
                html += '<div class="flex items-center gap-1">';
                html += '<a class="px-2 py-1 text-xs font-medium rounded bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300" href="/Dashboard/ProjectMemory/Playground?sessionId=' + sQ2 + '">Play</a>';
                html += '<a class="px-2 py-1 text-xs font-medium rounded bg-indigo-50 text-indigo-700 dark:bg-indigo-900/30 dark:text-indigo-300" href="/Dashboard/ProjectMemory/Pipeline?sessionId=' + sQ2 + '">Pipe</a>';
                html += '<button class="pm-proj-assign px-2 py-1 text-xs font-medium rounded bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300" data-id="' + esc(s.sessionId) + '">Assign</button>';
                html += '</div>';
            } else {
                var sQ3 = encodeURIComponent(s.sessionId);
                html += '<div class="flex items-center gap-1">';
                html += '<a class="px-2 py-1 text-xs font-medium rounded bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300" href="/Dashboard/ProjectMemory/Playground?sessionId=' + sQ3 + '">Play</a>';
                html += '<a class="px-2 py-1 text-xs font-medium rounded bg-indigo-50 text-indigo-700 dark:bg-indigo-900/30 dark:text-indigo-300" href="/Dashboard/ProjectMemory/Pipeline?sessionId=' + sQ3 + '">Pipe</a>';
                html += '<span class="text-xs text-gray-400">Select a project</span>';
                html += '</div>';
            }
            html += '</div>';
        }
        standalone.innerHTML = html;
        standalone.querySelectorAll('.pm-proj-assign').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var sid = btn.getAttribute('data-id');
                api('/api/chat/sessions/' + encodeURIComponent(sid) + '/project', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ projectId: activeProjectId })
                })
                    .then(refreshAll)
                    .catch(function (e) { status.textContent = e.message || 'Assign failed'; });
            });
        });
    }

    function refreshAll() {
        status.textContent = 'Loading...';
        return Promise.all([
            api('/api/chat/projects?limit=200'),
            api('/api/chat/sessions?standalone=true&limit=200'),
            activeProjectId ? api('/api/chat/projects/' + encodeURIComponent(activeProjectId) + '/sessions?limit=200') : Promise.resolve([])
        ])
            .then(function (res) {
                renderProjects(res[0] || []);
                renderStandaloneSessions(res[1] || []);
                renderProjectSessions(res[2] || []);
                status.textContent = '';
            })
            .catch(function (e) {
                status.textContent = e.message || 'Load failed';
            });
    }

    createBtn.addEventListener('click', function () {
        var name = String(nameInput.value || '').trim();
        if (!name) {
            status.textContent = 'Enter a project name.';
            return;
        }
        var scenarioId = String(scenarioSel.value || 'people').trim();
        createBtn.disabled = true;
        api('/api/chat/projects', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: name, scenarioId: scenarioId })
        })
            .then(function (p) {
                nameInput.value = '';
                activeProjectId = p && p.projectId ? p.projectId : activeProjectId;
                return refreshAll();
            })
            .finally(function () { createBtn.disabled = false; });
    });

    refreshBtn.addEventListener('click', refreshAll);

    loadScenarios().then(refreshAll).catch(function (e) { status.textContent = e.message || 'Load failed'; });
})();
