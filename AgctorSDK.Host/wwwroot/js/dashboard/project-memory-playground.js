/**
 * Project memory playground: chat transcript + SSE streaming (PRD-013), same session API as CodeGraph.
 */
(function () {
    var agentSel = document.getElementById('pm-play-agent');
    var projectSel = document.getElementById('pm-play-project');
    var projectNameInput = document.getElementById('pm-play-project-name');
    var scenarioSel = document.getElementById('pm-play-scenario');
    var newProjectBtn = document.getElementById('pm-play-new-project');
    var sessionSel = document.getElementById('pm-play-session');
    var newBtn = document.getElementById('pm-play-new-session');
    var refreshBtn = document.getElementById('pm-play-refresh');
    var copyLinkBtn = document.getElementById('pm-play-copy-link');
    var sendBtn = document.getElementById('pm-play-send');
    var input = document.getElementById('pm-play-input');
    var messages = document.getElementById('pm-play-messages');
    var status = document.getElementById('pm-play-status');
    var hint = document.getElementById('pm-play-hint');
    if (!agentSel || !projectSel || !projectNameInput || !scenarioSel || !newProjectBtn || !sessionSel || !newBtn || !refreshBtn || !copyLinkBtn || !sendBtn || !input || !messages || !status) return;

    var activeSessionId = null;
    var activeProjectId = null;

    function syncUrl() {
        var qp = new URLSearchParams(window.location.search);
        var agentId = agentSel.value || qp.get('agentId') || '';
        if (agentId) qp.set('agentId', agentId); else qp.delete('agentId');
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

    function renderChatMarkdown(text) {
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

    function normalizeRole(roleRaw) {
        return typeof roleRaw === 'number'
            ? (roleRaw === 0 ? 'user' : roleRaw === 1 ? 'assistant' : roleRaw === 2 ? 'system' : 'tool')
            : String(roleRaw || '').toLowerCase();
    }

    function roleLabel(role, turn) {
        if (role === 'user') return 'You';
        return turn && turn.agentId ? String(turn.agentId) : 'Assistant';
    }

    function renderTranscript(transcript) {
        var turns = transcript && transcript.turns ? transcript.turns : [];
        if (turns.length === 0) {
            messages.innerHTML = '<div class="text-gray-500 dark:text-gray-400 text-sm">No messages yet. Send a prompt below.</div>';
            return;
        }
        var html = '';
        turns.forEach(function (turn) {
            var role = normalizeRole(turn.role);
            var content = turn.content || '';
            var isUser = role === 'user';
            var bubble = isUser
                ? 'border-gray-200 bg-white dark:border-gray-600 dark:bg-gray-800 text-gray-800 dark:text-gray-100'
                : 'border-green-200 bg-green-50 dark:border-green-900 dark:bg-green-900/20 text-green-900 dark:text-green-100';
            html += '<div class="rounded-lg border p-3 ' + bubble + '">';
            html += '<div class="text-xs font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">' + esc(roleLabel(role, turn)) + '</div>';
            if (isUser) {
                html += '<div class="whitespace-pre-wrap break-words">' + esc(content) + '</div>';
            } else {
                html += '<div class="pm-play-md">' + renderChatMarkdown(content) + '</div>';
            }
            html += '</div>';
        });
        messages.innerHTML = html;
        messages.scrollTop = messages.scrollHeight;
    }

    function loadTranscript(sessionId) {
        if (!sessionId) return Promise.resolve();
        return fetch('/api/chat/sessions/' + encodeURIComponent(sessionId))
            .then(function (r) {
                if (!r.ok) throw new Error('Failed to load transcript');
                return r.json();
            })
            .then(renderTranscript);
    }

    function loadAgents() {
        var presetAgent = new URLSearchParams(window.location.search).get('agentId') || '';
        return fetch('/api/project-memory/agents')
            .then(function (r) {
                if (r.status === 400) return r.json().then(function (b) { throw new Error(b.error || 'Bad request'); });
                return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
            })
            .then(function (list) {
                agentSel.innerHTML = '';
                (list || []).forEach(function (a) {
                    var opt = document.createElement('option');
                    opt.value = a.id;
                    opt.textContent = a.id + (a.name ? ' — ' + a.name : '');
                    agentSel.appendChild(opt);
                });
                if (presetAgent) agentSel.value = presetAgent;
                if (!agentSel.options.length) {
                    var o = document.createElement('option');
                    o.value = '';
                    o.textContent = 'No agents';
                    agentSel.appendChild(o);
                }
            });
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

    function loadScenarios() {
        return fetch('/api/scenarios')
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('scenarios')); })
            .then(function (items) {
                scenarioSel.innerHTML = '';
                (items || []).forEach(function (s) {
                    var opt = document.createElement('option');
                    opt.value = s.id;
                    opt.textContent = (s.displayName || s.id) + ' [' + s.id + ']';
                    scenarioSel.appendChild(opt);
                });
                if (!scenarioSel.value && scenarioSel.options.length > 0) {
                    scenarioSel.value = 'people';
                }
            });
    }

    function createProject() {
        var name = String(projectNameInput.value || '').trim();
        if (!name) {
            status.textContent = 'Enter a project name (e.g. a person or business label).';
            return Promise.reject(new Error('missing name'));
        }
        var scenarioId = String(scenarioSel.value || 'people').trim();
        return fetch('/api/chat/projects', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ scenarioId: scenarioId, name: name })
        })
            .then(function (r) {
                if (!r.ok) throw new Error('Could not create project');
                return r.json();
            })
            .then(function (created) {
                projectNameInput.value = '';
                activeProjectId = created.projectId || '';
                return loadProjects(activeProjectId);
            });
    }

    function refreshSessions(preferredId) {
        var url = '/api/chat/sessions?limit=100';
        if (activeProjectId) url += '&projectId=' + encodeURIComponent(activeProjectId);
        return fetch(url)
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('sessions')); })
            .then(function (sessions) {
                sessionSel.innerHTML = '';
                (sessions || []).forEach(function (s) {
                    var opt = document.createElement('option');
                    opt.value = s.sessionId;
                    opt.textContent = (s.title || s.sessionId) + ' (' + s.turnCount + ')';
                    sessionSel.appendChild(opt);
                });
                var pick = preferredId || activeSessionId || (sessions && sessions[0] ? sessions[0].sessionId : null);
                if (pick) {
                    sessionSel.value = pick;
                    activeSessionId = pick;
                }
                syncUrl();
                if (hint) {
                    hint.textContent = activeProjectId
                        ? 'Project-selected sessions only. New session will be created inside this project.'
                        : 'All sessions shown (including standalone). Select/create a project to scope sessions.';
                }
                return activeSessionId;
            })
            .then(function (sid) { return loadTranscript(sid); });
    }

    /** First visit: create a PM Playground session if none exist (avoids empty dropdown). */
    function ensureSessionThenRefresh(urlSessionId) {
        var url = '/api/chat/sessions?limit=100';
        if (activeProjectId) url += '&projectId=' + encodeURIComponent(activeProjectId);
        return fetch(url)
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (sessions) {
                if (sessions && sessions.length > 0) {
                    return refreshSessions(urlSessionId || null);
                }
                return fetch('/api/chat/sessions', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ title: 'PM Playground', projectId: activeProjectId || null })
                })
                    .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('create session')); })
                    .then(function (created) { return refreshSessions(created.sessionId); });
            });
    }

    function createSession() {
        return fetch('/api/chat/sessions', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: 'PM Playground', projectId: activeProjectId || null })
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

    function extractSseEvents(buffer) {
        var events = [];
        var rest = buffer;
        var idx;
        while ((idx = rest.indexOf('\n\n')) >= 0) {
            var block = rest.slice(0, idx);
            rest = rest.slice(idx + 2);
            block.split('\n').forEach(function (line) {
                if (line.indexOf('data:') === 0) {
                    var j = line.slice(5).trim();
                    if (j) {
                        try { events.push(JSON.parse(j)); } catch (e2) { /* ignore */ }
                    }
                }
            });
        }
        return { rest: rest, events: events };
    }

    function sendStreaming() {
        var agentId = agentSel.value;
        var sessionId = sessionSel.value;
        var text = input.value.trim();
        if (!agentId) { status.textContent = 'Pick an agent spec.'; return; }
        if (!sessionId) { status.textContent = 'Pick or create a session.'; return; }
        if (!text) return;

        sendBtn.disabled = true;
        status.textContent = 'Streaming…';

        messages.insertAdjacentHTML(
            'beforeend',
            '<div class="rounded-lg border border-gray-200 bg-white dark:border-gray-600 dark:bg-gray-800 p-3">' +
            '<div class="text-xs font-semibold text-gray-500 dark:text-gray-400 mb-1">You</div>' +
            '<div class="whitespace-pre-wrap break-words text-gray-800 dark:text-gray-100">' + esc(text) + '</div></div>'
        );

        var sid = 'pm-stream-' + Date.now();
        var bubbleHtml =
            '<div id="' + sid + '" class="rounded-lg border border-green-200 bg-green-50 dark:border-green-900 dark:bg-green-900/20 p-3">' +
            '<div class="text-xs font-semibold text-green-800 dark:text-green-200 mb-1">Assistant</div>' +
            '<div class="pm-play-md text-sm text-green-900 dark:text-green-100" data-role="stream-body"></div></div>';
        messages.insertAdjacentHTML('beforeend', bubbleHtml);
        messages.scrollTop = messages.scrollHeight;

        var streamBody = document.querySelector('#' + sid + ' [data-role="stream-body"]');
        var acc = '';
        var markdownRaf = null;
        function applyMd() {
            if (!streamBody) return;
            try {
                streamBody.innerHTML = renderChatMarkdown(acc);
            } catch (e) {
                streamBody.textContent = acc;
            }
            messages.scrollTop = messages.scrollHeight;
        }
        function queueMd() {
            if (markdownRaf != null) return;
            markdownRaf = requestAnimationFrame(function () {
                markdownRaf = null;
                applyMd();
            });
        }

        fetch('/api/project-memory/playground/message/stream', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
            body: JSON.stringify({ sessionId: sessionId, agentId: agentId, payload: text })
        })
            .then(function (res) {
                if (!res.ok) return res.text().then(function (t) { throw new Error(t || String(res.status)); });
                if (!res.body) throw new Error('No stream body');
                input.value = '';
                var reader = res.body.getReader();
                var dec = new TextDecoder();
                var buf = '';
                function pump() {
                    return reader.read().then(function (result) {
                        if (result.value) buf += dec.decode(result.value, { stream: true });
                        var ex = extractSseEvents(buf);
                        buf = ex.rest;
                        for (var i = 0; i < ex.events.length; i++) {
                            var evt = ex.events[i];
                            if (evt.type === 'llm_delta' && evt.payload) {
                                acc += evt.payload;
                                queueMd();
                            }
                            if (evt.type === 'error' && evt.payload) {
                                acc += '\n[error] ' + evt.payload + '\n';
                                queueMd();
                            }
                        }
                        if (result.done) {
                            if (markdownRaf != null) cancelAnimationFrame(markdownRaf);
                            applyMd();
                            return;
                        }
                        return pump();
                    });
                }
                return pump();
            })
            .then(function () {
                status.textContent = 'Done';
                return loadTranscript(sessionId);
            })
            .catch(function (e) {
                status.textContent = 'Error';
                if (streamBody) streamBody.textContent = e.message || String(e);
            })
            .finally(function () {
                sendBtn.disabled = false;
            });
    }

    newBtn.addEventListener('click', function () {
        newBtn.disabled = true;
        createSession()
            .catch(function (e) { status.textContent = e.message || 'Failed'; })
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

    newProjectBtn.addEventListener('click', function () {
        newProjectBtn.disabled = true;
        createProject()
            .then(function () { return refreshSessions(null); })
            .catch(function (e) {
                if (e && e.message !== 'missing name') status.textContent = e.message || 'Create project failed';
            })
            .finally(function () { newProjectBtn.disabled = false; });
    });

    projectSel.addEventListener('change', function () {
        activeProjectId = projectSel.value || null;
        activeSessionId = null;
        syncUrl();
        refreshSessions(null).catch(function () { status.textContent = 'Project filter failed'; });
    });

    sessionSel.addEventListener('change', function () {
        activeSessionId = sessionSel.value;
        syncUrl();
        loadTranscript(activeSessionId);
    });

    sendBtn.addEventListener('click', sendStreaming);
    input.addEventListener('keydown', function (ev) {
        if (ev.key === 'Enter' && !ev.shiftKey) {
            ev.preventDefault();
            sendStreaming();
        }
    });

    var qs = new URLSearchParams(window.location.search);
    var qProject = qs.get('projectId');
    var qSession = qs.get('sessionId');
    loadAgents()
        .then(loadScenarios)
        .then(function () { return loadProjects(qProject || null); })
        .then(function () { return ensureSessionThenRefresh(qSession); })
        .catch(function (e) {
            status.textContent = e.message || 'Load failed';
            if (hint) hint.textContent = 'Set project root on Maintenance if agents fail to load.';
        });
})();

