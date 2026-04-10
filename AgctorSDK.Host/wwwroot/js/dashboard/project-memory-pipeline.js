/**
 * Phase 2 UI for deterministic orchestrator pipeline.
 */
(function () {
    var modeSel = document.getElementById('pm-pipe-mode');
    var projectSel = document.getElementById('pm-pipe-project');
    var sessionSel = document.getElementById('pm-pipe-session');
    var newBtn = document.getElementById('pm-pipe-new-session');
    var refreshBtn = document.getElementById('pm-pipe-refresh');
    var copyLinkBtn = document.getElementById('pm-pipe-copy-link');
    var runBtn = document.getElementById('pm-pipe-run');
    var input = document.getElementById('pm-pipe-input');
    var status = document.getElementById('pm-pipe-status');
    var hint = document.getElementById('pm-pipe-hint');
    var result = document.getElementById('pm-pipe-result');
    var steps = document.getElementById('pm-pipe-steps');
    if (!modeSel || !projectSel || !sessionSel || !newBtn || !refreshBtn || !copyLinkBtn || !runBtn || !input || !status || !result || !steps) return;

    var activeSessionId = null;
    var activeProjectId = null;

    function syncUrl() {
        var qp = new URLSearchParams(window.location.search);
        if (activeProjectId) qp.set('projectId', activeProjectId); else qp.delete('projectId');
        if (activeSessionId) qp.set('sessionId', activeSessionId); else qp.delete('sessionId');
        var next = window.location.pathname + (qp.toString() ? '?' + qp.toString() : '');
        window.history.replaceState({}, '', next);
    }

    function esc(s) {
        var d = document.createElement('div');
        d.textContent = s == null ? '' : String(s);
        return d.innerHTML;
    }

    function copyCurrentUrl() {
        var url = window.location.href;
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(url);
        }
        return new Promise(function (resolve, reject) {
            var el = document.createElement('textarea');
            el.value = url;
            el.setAttribute('readonly', '');
            el.style.position = 'absolute';
            el.style.left = '-9999px';
            document.body.appendChild(el);
            el.select();
            try {
                var ok = document.execCommand('copy');
                document.body.removeChild(el);
                if (ok) resolve(); else reject(new Error('Copy failed'));
            } catch (e) {
                document.body.removeChild(el);
                reject(e);
            }
        });
    }

    function renderMarkdown(text) {
        var source = String(text || '');
        if (window.marked && typeof window.marked.parse === 'function') {
            var raw = window.marked.parse(source, { gfm: true, breaks: true });
            if (window.DOMPurify && typeof window.DOMPurify.sanitize === 'function') {
                return window.DOMPurify.sanitize(raw);
            }
            return raw;
        }
        return esc(source);
    }

    function loadProjects(preferredId) {
        return fetch('/api/chat/projects?limit=100')
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('projects')); })
            .then(function (projects) {
                projectSel.innerHTML = '';
                var none = document.createElement('option');
                none.value = '';
                none.textContent = '(all sessions)';
                projectSel.appendChild(none);
                (projects || []).forEach(function (p) {
                    var opt = document.createElement('option');
                    opt.value = p.projectId;
                    opt.textContent = (p.name || p.projectId) + ' [' + (p.scenarioId || 'people') + ']';
                    projectSel.appendChild(opt);
                });
                activeProjectId = preferredId || activeProjectId || '';
                projectSel.value = activeProjectId;
                syncUrl();
            });
    }

    function refreshSessions(preferredId) {
        var url = '/api/chat/sessions?limit=100';
        if (activeProjectId) url += '&projectId=' + encodeURIComponent(activeProjectId);
        return fetch(url)
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('sessions')); })
            .then(function (sessions) {
                sessionSel.innerHTML = '';

                var none = document.createElement('option');
                none.value = '';
                none.textContent = '(none)';
                sessionSel.appendChild(none);

                (sessions || []).forEach(function (s) {
                    var opt = document.createElement('option');
                    opt.value = s.sessionId;
                    opt.textContent = (s.title || s.sessionId) + ' (' + s.turnCount + ')';
                    sessionSel.appendChild(opt);
                });

                var pick = preferredId || activeSessionId || '';
                sessionSel.value = pick;
                activeSessionId = pick || null;
                syncUrl();
                if (hint) {
                    hint.textContent = activeProjectId
                        ? 'Optional: choose a session from the selected project for prompt context.'
                        : 'Optional: choose any session for prompt context (or filter by project).';
                }
            });
    }

    function createSession() {
        return fetch('/api/chat/sessions', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: 'PM Pipeline', projectId: activeProjectId || null })
        })
            .then(function (r) {
                if (!r.ok) throw new Error('Could not create session');
                return r.json();
            })
            .then(function (created) {
                activeSessionId = created.sessionId;
                return refreshSessions(activeSessionId);
            });
    }

    function renderResult(payload) {
        var ok = !!payload.success;
        var cls = ok
            ? 'text-green-700 dark:text-green-300'
            : 'text-red-700 dark:text-red-300';
        result.innerHTML =
            '<div class="' + cls + ' font-medium">' + (ok ? 'Success' : 'Failed') + '</div>' +
            '<div class="mt-1 text-xs text-gray-500 dark:text-gray-400">Correlation: ' + esc(payload.correlationId || '') + '</div>' +
            '<div class="pm-pipe-md mt-2 break-words text-gray-800 dark:text-gray-100">' + renderMarkdown(payload.finalText || '') + '</div>';
    }

    function renderSteps(list) {
        if (!Array.isArray(list) || list.length === 0) {
            steps.innerHTML = '<div class="text-gray-500 dark:text-gray-400">No step data.</div>';
            return;
        }

        var html = '';
        for (var i = 0; i < list.length; i++) {
            var s = list[i] || {};
            var ok = !!s.ok;
            var border = ok
                ? 'border-green-200 dark:border-green-800 bg-green-50 dark:bg-green-900/20'
                : 'border-red-200 dark:border-red-800 bg-red-50 dark:bg-red-900/20';
            var badge = ok
                ? '<span class="text-green-800 dark:text-green-200">OK</span>'
                : '<span class="text-red-800 dark:text-red-200">FAIL</span>';
            html += '<div class="rounded border p-3 ' + border + '">';
            html += '<div class="flex items-center justify-between">';
            html += '<div class="font-semibold text-gray-900 dark:text-white">' + esc(s.name || '(step)') + '</div>';
            html += '<div class="text-xs font-semibold">' + badge + '</div>';
            html += '</div>';
            if (s.detail) {
                html += '<div class="pm-pipe-md mt-1 text-xs text-gray-700 dark:text-gray-200 break-words">' + renderMarkdown(s.detail) + '</div>';
            }
            if (Array.isArray(s.updatedFiles) && s.updatedFiles.length > 0) {
                html += '<div class="mt-2 text-xs text-gray-600 dark:text-gray-300">Updated files:</div>';
                html += '<ul class="mt-1 list-disc pl-5 text-xs text-gray-700 dark:text-gray-200">';
                for (var j = 0; j < s.updatedFiles.length; j++) {
                    var rel = String(s.updatedFiles[j] || '').replace(/\\/g, '/');
                    var marker = '/people/';
                    var idx = rel.indexOf(marker);
                    if (idx >= 0) rel = rel.slice(idx + 1);
                    var href = '/Dashboard/ProjectMemory/Workspace?path=' + encodeURIComponent(rel);
                    html += '<li class="font-mono break-all"><a class="text-blue-700 hover:underline dark:text-blue-300" href="' + href + '">' + esc(s.updatedFiles[j]) + '</a></li>';
                }
                html += '</ul>';
            }
            html += '</div>';
        }
        steps.innerHTML = html;
    }

    function runPipeline() {
        var text = input.value.trim();
        if (!text) {
            status.textContent = 'Enter a message first.';
            return;
        }

        runBtn.disabled = true;
        status.textContent = 'Running…';
        steps.innerHTML = '';
        result.textContent = 'Running pipeline...';

        var body = {
            userMessage: text,
            mode: modeSel.value || 'auto'
        };
        if (sessionSel.value) body.sessionId = sessionSel.value;

        fetch('/api/project-memory/orchestrator/run', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        })
            .then(function (r) {
                if (!r.ok) return r.text().then(function (t) { throw new Error(t || String(r.status)); });
                return r.json();
            })
            .then(function (payload) {
                renderResult(payload || {});
                renderSteps(payload && payload.steps ? payload.steps : []);
                status.textContent = 'Done';
            })
            .catch(function (e) {
                status.textContent = 'Error';
                result.innerHTML = '<div class="text-red-700 dark:text-red-300">' + esc(e.message || 'Failed') + '</div>';
                steps.innerHTML = '';
            })
            .finally(function () {
                runBtn.disabled = false;
            });
    }

    newBtn.addEventListener('click', function () {
        newBtn.disabled = true;
        createSession()
            .catch(function (e) { status.textContent = e.message || 'Create session failed'; })
            .finally(function () { newBtn.disabled = false; });
    });

    refreshBtn.addEventListener('click', function () {
        refreshSessions(sessionSel.value).catch(function () { status.textContent = 'Refresh failed'; });
    });

    copyLinkBtn.addEventListener('click', function () {
        copyCurrentUrl()
            .then(function () { status.textContent = 'Link copied'; })
            .catch(function () { status.textContent = 'Copy link failed'; });
    });

    projectSel.addEventListener('change', function () {
        activeProjectId = projectSel.value || null;
        activeSessionId = null;
        syncUrl();
        refreshSessions(null).catch(function () { status.textContent = 'Project filter failed'; });
    });

    sessionSel.addEventListener('change', function () {
        activeSessionId = sessionSel.value || null;
        syncUrl();
    });

    runBtn.addEventListener('click', runPipeline);
    input.addEventListener('keydown', function (ev) {
        if (ev.key === 'Enter' && (ev.metaKey || ev.ctrlKey)) {
            ev.preventDefault();
            runPipeline();
        }
    });

    var qs = new URLSearchParams(window.location.search);
    var qProject = qs.get('projectId');
    var qSession = qs.get('sessionId');
    loadProjects(qProject || null).then(function () { return refreshSessions(qSession || null); }).catch(function () {
        status.textContent = 'Could not load sessions';
    });
})();
