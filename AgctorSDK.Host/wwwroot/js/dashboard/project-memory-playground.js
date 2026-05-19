/**
 * Project memory playground: chat transcript + SSE streaming (PRD-013).
 * PRD-017: project rail + session list + project-scoped scenario; debug panels below transcript.
 * Session column (transcript + debug) stays hidden until a session is selected — no auto-pick of the first row.
 * Last selected session per project is restored from localStorage when the URL does not pin a session.
 */
(function () {
    var agentSel = document.getElementById('pm-play-agent');
    var projectNameInput = document.getElementById('pm-play-project-name');
    var scenarioNewSel = document.getElementById('pm-play-scenario-new');
    var scenarioChangeSel = document.getElementById('pm-play-scenario-change');
    var newProjectBtn = document.getElementById('pm-play-new-project');
    var projectListEl = document.getElementById('pm-play-project-list');
    var sessionListEl = document.getElementById('pm-play-session-list');
    var newBtn = document.getElementById('pm-play-new-session');
    var refreshBtn = document.getElementById('pm-play-refresh');
    var sendBtn = document.getElementById('pm-play-send');
    var input = document.getElementById('pm-play-input');
    var messages = document.getElementById('pm-play-messages');
    var status = document.getElementById('pm-play-status');
    var hint = document.getElementById('pm-play-hint');
    var flowStepsEl = document.getElementById('pm-play-flow-steps');
    var flowDetailEl = document.getElementById('pm-play-flow-detail');
    var projectHeaderEl = document.getElementById('pm-play-project-header');
    var projectTitleEl = document.getElementById('pm-play-project-title');
    var scenarioLabelEl = document.getElementById('pm-play-project-scenario-label');
    var changeScenarioBtn = document.getElementById('pm-play-change-scenario');
    var scenarioPanelEl = document.getElementById('pm-play-scenario-change-panel');
    var scenarioApplyBtn = document.getElementById('pm-play-scenario-apply');
    var scenarioCancelBtn = document.getElementById('pm-play-scenario-cancel');
    var advancedEl = document.getElementById('pm-play-advanced');
    var agentAutoLabelEl = document.getElementById('pm-play-agent-auto-label');
    var agentResetBtn = document.getElementById('pm-play-agent-reset');
    var sessionDetailEl = document.getElementById('pm-play-session-detail');
    var noSessionEl = document.getElementById('pm-play-no-session');

    if (
        !agentSel ||
        !projectNameInput ||
        !scenarioNewSel ||
        !scenarioChangeSel ||
        !newProjectBtn ||
        !projectListEl ||
        !sessionListEl ||
        !newBtn ||
        !refreshBtn ||
        !sendBtn ||
        !input ||
        !messages ||
        !status ||
        !sessionDetailEl ||
        !noSessionEl
    ) {
        return;
    }

    /** Last project list from API; used for labels without an extra round-trip. */
    var projectsCache = [];
    /** Catalog rows from GET /api/scenarios (includes personaBindings + personaAgentIds + flow). */
    var scenariosCache = [];
    /** Global agent list from GET /api/project-memory/agents (labels). */
    var agentsCache = [];
    var activeSessionId = null;
    var activeProjectId = null;
    /** Scenario id for the active project (stream body + header). */
    var activeProjectScenarioId = '';
    /** Operator-picked override (cleared on scenario/project change and by Reset). */
    var agentOverrideId = '';
    /** Project id currently in inline-rename mode in the Projects rail (null = none). */
    var renamingProjectId = null;
    /** Session id currently in inline-rename mode in the Sessions column (null = none). */
    var renamingSessionId = null;
    /** Session id currently in inline-delete-confirm state (null = none). */
    var confirmingDeleteSessionId = null;
    /** Last session list rendered (cached for in-place re-renders after rename). */
    var sessionsCache = [];

    /** Namespace prefix so we do not collide with other app keys. */
    var LS_LAST_SESSION_PREFIX = 'agctor.pm-play.lastSession.';

    function lastSessionStorageKey(projectId) {
        return LS_LAST_SESSION_PREFIX + String(projectId || '');
    }

    /** Returns a session id previously chosen for this project, or null (private mode / missing). */
    function readStoredLastSessionId(projectId) {
        if (!projectId) return null;
        try {
            var v = localStorage.getItem(lastSessionStorageKey(projectId));
            var s = v && String(v).trim();
            return s || null;
        } catch (_) {
            return null;
        }
    }

    /** Persists or clears the remembered session for a project (cleared when project has no sessions / none selected). */
    function persistLastSessionForProject(projectId, sessionId) {
        if (!projectId) return;
        try {
            var k = lastSessionStorageKey(projectId);
            if (sessionId) localStorage.setItem(k, sessionId);
            else localStorage.removeItem(k);
        } catch (_) {
            /* quota / disabled storage */
        }
    }

    function syncUrl() {
        var qp = new URLSearchParams(window.location.search);
        // Only persist an explicit override in the URL; auto-defaults stay implicit.
        if (agentOverrideId) qp.set('agentId', agentOverrideId);
        else qp.delete('agentId');
        if (activeProjectId) qp.set('projectId', activeProjectId);
        else qp.delete('projectId');
        if (activeSessionId) qp.set('sessionId', activeSessionId);
        else qp.delete('sessionId');
        var next = window.location.pathname + (qp.toString() ? '?' + qp.toString() : '');
        window.history.replaceState({}, '', next);
    }

    function esc(s) {
        var d = document.createElement('div');
        d.textContent = s == null ? '' : String(s);
        return d.innerHTML;
    }

    function scenarioDisplayName(id) {
        var sid = String(id || '').trim();
        for (var i = 0; i < scenariosCache.length; i++) {
            if (scenariosCache[i].id === sid) {
                return (scenariosCache[i].displayName || scenariosCache[i].id) + ' (' + sid + ')';
            }
        }
        return sid || '—';
    }

    function fillScenarioSelects() {
        scenarioNewSel.innerHTML = '';
        scenarioChangeSel.innerHTML = '';
        scenariosCache.forEach(function (s) {
            var o1 = document.createElement('option');
            o1.value = s.id;
            o1.textContent = (s.displayName || s.id) + ' [' + s.id + ']';
            scenarioNewSel.appendChild(o1);
            var o2 = document.createElement('option');
            o2.value = s.id;
            o2.textContent = (s.displayName || s.id) + ' [' + s.id + ']';
            scenarioChangeSel.appendChild(o2);
        });
        if (!scenarioNewSel.value && scenarioNewSel.options.length > 0) {
            scenarioNewSel.value = 'people';
        }
    }

    function setScenarioPanelVisible(show) {
        if (!scenarioPanelEl) return;
        if (show) scenarioPanelEl.classList.remove('hidden');
        else scenarioPanelEl.classList.add('hidden');
    }

    /**
     * Toggle the right column: full playground chrome only when a session is active.
     * Avoids showing transcript / scenario / debug for an implicit "first list item" selection.
     */
    function syncSessionColumn() {
        var has = !!activeSessionId;
        if (has) {
            noSessionEl.classList.add('hidden');
            sessionDetailEl.classList.remove('hidden');
            input.disabled = false;
            sendBtn.disabled = false;
            if (agentSel) agentSel.disabled = false;
        } else {
            noSessionEl.classList.remove('hidden');
            sessionDetailEl.classList.add('hidden');
            messages.innerHTML = '';
            resetFlowViz();
            if (flowDetailEl) flowDetailEl.textContent = '';
            status.textContent = '';
            input.value = '';
            input.disabled = true;
            sendBtn.disabled = true;
            if (agentSel) agentSel.disabled = true;
            setScenarioPanelVisible(false);
            if (window.agctorTraceTimeline) {
                var traceRoot = document.getElementById('pm-play-trace-timeline');
                var emptyMsg = traceRoot && traceRoot.dataset ? traceRoot.dataset.emptyMessage : '';
                window.agctorTraceTimeline.clear(
                    'pm-play-trace-timeline',
                    emptyMsg || 'Send a message to see LLM, ingest, and persist spans for this request.',
                    'No session selected'
                );
            }
        }
    }

    function updateProjectHeader() {
        if (!projectHeaderEl || !projectTitleEl || !scenarioLabelEl) return;
        if (!activeProjectId) {
            projectHeaderEl.classList.add('hidden');
            return;
        }
        var p = null;
        for (var i = 0; i < projectsCache.length; i++) {
            if (projectsCache[i].projectId === activeProjectId) {
                p = projectsCache[i];
                break;
            }
        }
        var title = p ? p.name || p.projectId : activeProjectId;
        projectTitleEl.textContent = title;
        scenarioLabelEl.textContent =
            'Current scenario for this project: ' + scenarioDisplayName(activeProjectScenarioId || (p && p.scenarioId) || '');
        projectHeaderEl.classList.remove('hidden');
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
            ? roleRaw === 0
                ? 'user'
                : roleRaw === 1
                  ? 'assistant'
                  : roleRaw === 2
                    ? 'system'
                    : 'tool'
            : String(roleRaw || '').toLowerCase();
    }

    function roleLabel(role, turn) {
        if (role === 'user') return 'You';
        return turn && turn.agentId ? String(turn.agentId) : 'Assistant';
    }

    function renderTranscript(transcript) {
        var turns = transcript && transcript.turns ? transcript.turns : [];
        if (turns.length === 0) {
            messages.innerHTML =
                '<div class="text-gray-500 dark:text-gray-400 text-sm">No messages yet. Send a prompt below.</div>';
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
            html +=
                '<div class="text-xs font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400 mb-1">' +
                esc(roleLabel(role, turn)) +
                '</div>';
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
        if (!sessionId) {
            return Promise.resolve();
        }
        return fetch('/api/chat/sessions/' + encodeURIComponent(sessionId))
            .then(function (r) {
                if (!r.ok) throw new Error('Failed to load transcript');
                var ct = (r.headers.get('content-type') || '').toLowerCase();
                if (ct.indexOf('json') < 0) {
                    return r.text().then(function (t) {
                        throw new Error(t || 'Session response was not JSON');
                    });
                }
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
                agentsCache = list || [];
                // If an override is present in the URL, respect it until the user resets.
                if (presetAgent) agentOverrideId = presetAgent;
            });
    }

    function loadScenarios() {
        return fetch('/api/scenarios')
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('scenarios')); })
            .then(function (items) {
                scenariosCache = items || [];
                fillScenarioSelects();
            });
    }

    /** Lookup full scenario DTO (PersonaBindings, personaAgentIds, flow). */
    function scenarioFor(id) {
        var sid = String(id || '').trim();
        for (var i = 0; i < scenariosCache.length; i++) {
            if (scenariosCache[i].id === sid) return scenariosCache[i];
        }
        return null;
    }

    /** Human label for an agent id from the global catalog. */
    function agentLabel(id) {
        for (var i = 0; i < agentsCache.length; i++) {
            if (agentsCache[i].id === id) {
                return agentsCache[i].id + (agentsCache[i].name ? ' — ' + agentsCache[i].name : '');
            }
        }
        return String(id || '');
    }

    /**
     * Collect agent ids available for the active scenario.
     * Priority: personaAgentIds (scenario roster) → flow LlmNode config.personaId → empty = global list.
     */
    function scopedAgentIdsForActive() {
        var scen = scenarioFor(activeProjectScenarioId);
        if (!scen) return [];
        var ids = [];
        (scen.personaAgentIds || []).forEach(function (id) {
            if (id && ids.indexOf(id) < 0) ids.push(id);
        });
        var nodes = (scen.flow && scen.flow.nodes) || [];
        nodes.forEach(function (n) {
            if (n && n.type === 'LlmNode' && n.config && n.config.personaId && ids.indexOf(n.config.personaId) < 0) {
                ids.push(n.config.personaId);
            }
        });
        return ids;
    }

    /** Scenario-default agent: Extractor binding wins (ingest-capable), else first scoped id. */
    function defaultAgentIdForActive() {
        var scen = scenarioFor(activeProjectScenarioId);
        if (scen && scen.personaBindings && scen.personaBindings.extractor) {
            return String(scen.personaBindings.extractor).trim();
        }
        var scoped = scopedAgentIdsForActive();
        if (scoped.length > 0) return scoped[0];
        return agentsCache.length > 0 ? agentsCache[0].id : '';
    }

    /**
     * Rebuild the agent select using scenario-scoped options (fallback: global list).
     * Resolves effective agent = override (if still valid) else scenario default,
     * then updates the Advanced summary caption.
     */
    function refreshAgentSelection() {
        if (!agentSel) return;
        var scoped = scopedAgentIdsForActive();
        var options = scoped.length > 0
            ? scoped
            : agentsCache.map(function (a) { return a.id; });
        agentSel.innerHTML = '';
        if (options.length === 0) {
            var empty = document.createElement('option');
            empty.value = '';
            empty.textContent = 'No agents';
            agentSel.appendChild(empty);
        } else {
            options.forEach(function (id) {
                var opt = document.createElement('option');
                opt.value = id;
                opt.textContent = agentLabel(id);
                agentSel.appendChild(opt);
            });
        }

        var effectiveDefault = defaultAgentIdForActive();
        var effective =
            agentOverrideId && options.indexOf(agentOverrideId) >= 0 ? agentOverrideId : effectiveDefault;
        // Drop an override that's no longer valid for this scenario.
        if (agentOverrideId && options.indexOf(agentOverrideId) < 0) agentOverrideId = '';
        if (effective) agentSel.value = effective;

        if (agentAutoLabelEl) {
            if (agentOverrideId && agentOverrideId === effective) {
                agentAutoLabelEl.textContent = 'override: ' + effective;
            } else if (effective) {
                agentAutoLabelEl.textContent = 'auto: ' + effective;
            } else {
                agentAutoLabelEl.textContent = '';
            }
        }
        syncUrl();
    }

    /** Renders a normal project row (select + Rename affordance for the active row). */
    function renderProjectRow(p) {
        var isActive = p.projectId === activeProjectId;
        var scen = (p.scenarioId || 'people').trim();

        var row = document.createElement('div');
        row.className = 'pm-play-project-row relative group';
        row.dataset.projectId = p.projectId;

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'pm-play-list-btn text-gray-800 dark:text-gray-100 w-full pr-14';
        if (isActive) btn.classList.add('pm-play-active');
        btn.setAttribute('role', 'option');
        btn.setAttribute('aria-selected', isActive ? 'true' : 'false');
        btn.innerHTML =
            '<div class="font-medium truncate">' +
            esc(p.name || p.projectId) +
            '</div><div class="text-[11px] text-gray-500 dark:text-gray-400 truncate">' +
            esc(scen) +
            '</div>';
        btn.addEventListener('click', function () {
            selectProject(p.projectId);
        });
        row.appendChild(btn);

        // Rename only for the active project to keep project-level intent unambiguous.
        if (isActive) {
            var renameLink = document.createElement('button');
            renameLink.type = 'button';
            renameLink.className =
                'absolute right-2 top-1/2 -translate-y-1/2 opacity-0 group-hover:opacity-100 focus:opacity-100 transition-opacity text-[11px] font-medium text-blue-700 hover:underline dark:text-blue-300';
            renameLink.textContent = 'Rename';
            renameLink.setAttribute('aria-label', 'Rename project');
            renameLink.addEventListener('click', function (ev) {
                ev.stopPropagation();
                startRename(p.projectId);
            });
            row.appendChild(renameLink);
        }
        return row;
    }

    /** Renders the edit-in-place form (input + Save + Cancel) for a project row. */
    function renderProjectEditor(p) {
        var wrap = document.createElement('div');
        wrap.className =
            'pm-play-project-row rounded-lg border border-blue-300 bg-blue-50 p-2 space-y-2 dark:border-blue-500 dark:bg-blue-900/20';
        wrap.dataset.projectId = p.projectId;

        var input = document.createElement('input');
        input.type = 'text';
        input.className =
            'w-full rounded-lg border border-gray-300 bg-white p-2 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white';
        input.value = p.name || p.projectId;
        input.setAttribute('aria-label', 'New project name');
        input.addEventListener('keydown', function (ev) {
            if (ev.key === 'Enter') {
                ev.preventDefault();
                applyRename(p.projectId, input.value);
            } else if (ev.key === 'Escape') {
                ev.preventDefault();
                cancelRename();
            }
        });

        var actions = document.createElement('div');
        actions.className = 'flex items-center gap-2';
        var save = document.createElement('button');
        save.type = 'button';
        save.className = 'px-2.5 py-1 text-xs font-medium text-white bg-blue-600 rounded hover:bg-blue-700';
        save.textContent = 'Save';
        save.addEventListener('click', function () {
            save.disabled = true;
            applyRename(p.projectId, input.value).finally(function () { save.disabled = false; });
        });
        var cancel = document.createElement('button');
        cancel.type = 'button';
        cancel.className =
            'px-2.5 py-1 text-xs font-medium text-gray-700 bg-gray-100 rounded hover:bg-gray-200 dark:bg-gray-600 dark:text-gray-100';
        cancel.textContent = 'Cancel';
        cancel.addEventListener('click', cancelRename);
        actions.appendChild(save);
        actions.appendChild(cancel);

        wrap.appendChild(input);
        wrap.appendChild(actions);

        // Defer focus until the element is in the DOM.
        setTimeout(function () { input.focus(); input.select(); }, 0);
        return wrap;
    }

    function renderProjectList() {
        projectListEl.innerHTML = '';
        if (!projectsCache.length) {
            projectListEl.innerHTML =
                '<div class="text-xs text-gray-500 dark:text-gray-400">No projects yet. Create one below.</div>';
            return;
        }
        projectsCache.forEach(function (p) {
            var node = renamingProjectId === p.projectId
                ? renderProjectEditor(p)
                : renderProjectRow(p);
            projectListEl.appendChild(node);
        });
    }

    function startRename(projectId) {
        if (!projectId) return;
        renamingProjectId = projectId;
        renderProjectList();
    }

    function cancelRename() {
        renamingProjectId = null;
        renderProjectList();
    }

    function selectProject(projectId) {
        renamingProjectId = null;
        renamingSessionId = null;
        confirmingDeleteSessionId = null;
        activeProjectId = projectId || null;
        activeSessionId = null;
        var p = null;
        for (var i = 0; i < projectsCache.length; i++) {
            if (projectsCache[i].projectId === activeProjectId) {
                p = projectsCache[i];
                break;
            }
        }
        activeProjectScenarioId = p ? String(p.scenarioId || 'people').trim() : '';
        // Moving to a different project clears a sticky override (safer default).
        agentOverrideId = '';
        renderProjectList();
        updateProjectHeader();
        refreshAgentSelection();
        syncUrl();
        if (scenarioChangeSel && activeProjectScenarioId) scenarioChangeSel.value = activeProjectScenarioId;
        return loadSessionsForProject(null);
    }

    function loadProjects(preferredId, options) {
        options = options || {};
        var skipDefaultFirst = !!options.skipDefaultFirst;
        return fetch('/api/chat/projects?limit=100')
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('projects')); })
            .then(function (projects) {
                projectsCache = projects || [];
                var inList = preferredId && projectsCache.some(function (x) { return x.projectId === preferredId; });
                // Default to first project when nothing valid is selected (empty landing / stale projectId in URL).
                // skipDefaultFirst: deep-linked session is not tied to a project — keep project unselected for orphan flow.
                if (inList) {
                    activeProjectId = preferredId;
                } else if (!skipDefaultFirst && projectsCache.length > 0) {
                    activeProjectId = projectsCache[0].projectId;
                } else {
                    activeProjectId = null;
                }
                var p = null;
                for (var j = 0; j < projectsCache.length; j++) {
                    if (projectsCache[j].projectId === activeProjectId) {
                        p = projectsCache[j];
                        break;
                    }
                }
                activeProjectScenarioId = p ? String(p.scenarioId || 'people').trim() : '';
                renderProjectList();
                updateProjectHeader();
                refreshAgentSelection();
                syncUrl();
            });
    }

    /** Renders one session row with a hover-only Rename link on the active row. */
    function renderSessionRow(s, isActive) {
        var row = document.createElement('div');
        row.className = 'pm-play-session-row relative group';
        row.dataset.sessionId = s.sessionId;

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'pm-play-list-btn text-gray-800 dark:text-gray-100 w-full pr-14';
        if (isActive) btn.classList.add('pm-play-active');
        btn.setAttribute('role', 'option');
        btn.setAttribute('aria-selected', isActive ? 'true' : 'false');
        btn.innerHTML =
            '<div class="font-medium truncate">' +
            esc(s.title || s.sessionId) +
            '</div><div class="text-[11px] text-gray-500 dark:text-gray-400">' +
            esc(String(s.turnCount != null ? s.turnCount : 0)) +
            ' turns</div>';
        btn.addEventListener('click', function () {
            renamingSessionId = null;
            confirmingDeleteSessionId = null;
            activeSessionId = s.sessionId;
            renderSessionList(sessionsCache, activeSessionId);
        });
        row.appendChild(btn);

        // Rename + Delete live on the active session row only to keep actions unambiguous.
        if (isActive) {
            var actions = document.createElement('div');
            actions.className =
                'absolute right-2 top-1/2 -translate-y-1/2 flex items-center gap-2 opacity-0 group-hover:opacity-100 focus-within:opacity-100 transition-opacity';

            var renameLink = document.createElement('button');
            renameLink.type = 'button';
            renameLink.className = 'text-[11px] font-medium text-blue-700 hover:underline dark:text-blue-300';
            renameLink.textContent = 'Rename';
            renameLink.setAttribute('aria-label', 'Rename session');
            renameLink.addEventListener('click', function (ev) {
                ev.stopPropagation();
                startSessionRename(s.sessionId);
            });

            var deleteLink = document.createElement('button');
            deleteLink.type = 'button';
            deleteLink.className = 'text-[11px] font-medium text-red-700 hover:underline dark:text-red-300';
            deleteLink.textContent = 'Delete';
            deleteLink.setAttribute('aria-label', 'Delete session');
            deleteLink.addEventListener('click', function (ev) {
                ev.stopPropagation();
                startSessionDelete(s.sessionId);
            });

            actions.appendChild(renameLink);
            actions.appendChild(deleteLink);
            row.appendChild(actions);
        }
        return row;
    }

    /** Inline delete-confirm row: "Delete 'title'? [Delete] [Cancel]" with red primary. */
    function renderSessionDeleteConfirm(s) {
        var wrap = document.createElement('div');
        wrap.className =
            'pm-play-session-row rounded-lg border border-red-300 bg-red-50 p-2 space-y-2 dark:border-red-500 dark:bg-red-900/20';
        wrap.dataset.sessionId = s.sessionId;

        var msg = document.createElement('div');
        msg.className = 'text-xs text-red-900 dark:text-red-200';
        msg.innerHTML =
            'Delete <strong>' +
            esc(s.title || s.sessionId) +
            '</strong>? This removes its transcript and trace links.';
        wrap.appendChild(msg);

        var actions = document.createElement('div');
        actions.className = 'flex items-center gap-2';
        var confirm = document.createElement('button');
        confirm.type = 'button';
        confirm.className = 'px-2.5 py-1 text-xs font-medium text-white bg-red-600 rounded hover:bg-red-700';
        confirm.textContent = 'Delete';
        confirm.addEventListener('click', function () {
            confirm.disabled = true;
            applySessionDelete(s.sessionId).finally(function () { confirm.disabled = false; });
        });
        var cancel = document.createElement('button');
        cancel.type = 'button';
        cancel.className =
            'px-2.5 py-1 text-xs font-medium text-gray-700 bg-gray-100 rounded hover:bg-gray-200 dark:bg-gray-600 dark:text-gray-100';
        cancel.textContent = 'Cancel';
        cancel.addEventListener('click', cancelSessionDelete);
        actions.appendChild(confirm);
        actions.appendChild(cancel);
        wrap.appendChild(actions);

        // Keyboard: Enter confirms, Esc cancels (focus the confirm button).
        setTimeout(function () { confirm.focus(); }, 0);
        wrap.addEventListener('keydown', function (ev) {
            if (ev.key === 'Escape') { ev.preventDefault(); cancelSessionDelete(); }
            else if (ev.key === 'Enter') { ev.preventDefault(); confirm.click(); }
        });
        wrap.tabIndex = -1;
        return wrap;
    }

    /** Inline editor for renaming a session row. */
    function renderSessionEditor(s) {
        var wrap = document.createElement('div');
        wrap.className =
            'pm-play-session-row rounded-lg border border-blue-300 bg-blue-50 p-2 space-y-2 dark:border-blue-500 dark:bg-blue-900/20';
        wrap.dataset.sessionId = s.sessionId;

        var input = document.createElement('input');
        input.type = 'text';
        input.className =
            'w-full rounded-lg border border-gray-300 bg-white p-2 text-sm dark:bg-gray-700 dark:border-gray-600 dark:text-white';
        input.value = s.title || s.sessionId;
        input.setAttribute('aria-label', 'New session title');
        input.addEventListener('keydown', function (ev) {
            if (ev.key === 'Enter') {
                ev.preventDefault();
                applySessionRename(s.sessionId, input.value);
            } else if (ev.key === 'Escape') {
                ev.preventDefault();
                cancelSessionRename();
            }
        });

        var actions = document.createElement('div');
        actions.className = 'flex items-center gap-2';
        var save = document.createElement('button');
        save.type = 'button';
        save.className = 'px-2.5 py-1 text-xs font-medium text-white bg-blue-600 rounded hover:bg-blue-700';
        save.textContent = 'Save';
        save.addEventListener('click', function () {
            save.disabled = true;
            applySessionRename(s.sessionId, input.value).finally(function () { save.disabled = false; });
        });
        var cancel = document.createElement('button');
        cancel.type = 'button';
        cancel.className =
            'px-2.5 py-1 text-xs font-medium text-gray-700 bg-gray-100 rounded hover:bg-gray-200 dark:bg-gray-600 dark:text-gray-100';
        cancel.textContent = 'Cancel';
        cancel.addEventListener('click', cancelSessionRename);
        actions.appendChild(save);
        actions.appendChild(cancel);

        wrap.appendChild(input);
        wrap.appendChild(actions);
        setTimeout(function () { input.focus(); input.select(); }, 0);
        return wrap;
    }

    function renderSessionList(sessions, preferredSessionId) {
        sessionsCache = sessions || [];
        sessionListEl.innerHTML = '';
        if (!activeProjectId) {
            sessionListEl.innerHTML =
                '<div class="text-xs text-gray-500 dark:text-gray-400">Select a project to list its sessions.</div>';
            if (hint) hint.textContent = '';
            newBtn.disabled = true;
            activeSessionId = null;
            syncSessionColumn();
            syncUrl();
            return Promise.resolve();
        }
        newBtn.disabled = false;
        if (!sessionsCache.length) {
            sessionListEl.innerHTML =
                '<div class="text-xs text-gray-500 dark:text-gray-400">No sessions yet. Click New session.</div>';
            if (hint) hint.textContent = 'Sessions belong to this project only.';
            activeSessionId = null;
            persistLastSessionForProject(activeProjectId, null);
            syncUrl();
            syncSessionColumn();
            return Promise.resolve();
        }
        // Prefer URL/explicit pick, then in-memory selection, then localStorage for this project — never auto-select first row.
        var pick = null;
        if (
            preferredSessionId &&
            sessionsCache.some(function (s) { return s.sessionId === preferredSessionId; })
        ) {
            pick = preferredSessionId;
        } else if (
            activeSessionId &&
            sessionsCache.some(function (s2) { return s2.sessionId === activeSessionId; })
        ) {
            pick = activeSessionId;
        } else {
            var stored = readStoredLastSessionId(activeProjectId);
            if (stored && sessionsCache.some(function (s3) { return s3.sessionId === stored; })) {
                pick = stored;
            }
        }
        sessionsCache.forEach(function (s) {
            var node;
            if (confirmingDeleteSessionId === s.sessionId) node = renderSessionDeleteConfirm(s);
            else if (renamingSessionId === s.sessionId) node = renderSessionEditor(s);
            else node = renderSessionRow(s, s.sessionId === pick);
            sessionListEl.appendChild(node);
        });
        activeSessionId = pick;
        persistLastSessionForProject(activeProjectId, activeSessionId);
        syncUrl();
        syncSessionColumn();
        if (hint) hint.textContent = 'Sessions belong to this project only.';
        return activeSessionId ? loadTranscript(activeSessionId) : Promise.resolve();
    }

    function startSessionRename(sessionId) {
        if (!sessionId) return;
        confirmingDeleteSessionId = null;
        renamingSessionId = sessionId;
        renderSessionList(sessionsCache, activeSessionId);
    }

    function cancelSessionRename() {
        renamingSessionId = null;
        renderSessionList(sessionsCache, activeSessionId);
    }

    function startSessionDelete(sessionId) {
        if (!sessionId) return;
        renamingSessionId = null;
        confirmingDeleteSessionId = sessionId;
        renderSessionList(sessionsCache, activeSessionId);
    }

    function cancelSessionDelete() {
        confirmingDeleteSessionId = null;
        renderSessionList(sessionsCache, activeSessionId);
    }

    /** DELETEs the session, removes it from cache, and re-selects a neighbor or none. */
    function applySessionDelete(sessionId) {
        if (!sessionId) return Promise.resolve();
        return fetch('/api/chat/sessions/' + encodeURIComponent(sessionId), { method: 'DELETE' })
            .then(function (r) {
                if (!r.ok && r.status !== 204) throw new Error('Could not delete session');
            })
            .then(function () {
                // Pick the next neighbor in the list to select after removal (stable feel).
                var idx = -1;
                for (var i = 0; i < sessionsCache.length; i++) {
                    if (sessionsCache[i].sessionId === sessionId) { idx = i; break; }
                }
                sessionsCache = sessionsCache.filter(function (s) { return s.sessionId !== sessionId; });
                var nextPick = null;
                if (sessionsCache.length > 0) {
                    var neighbor = sessionsCache[Math.min(idx, sessionsCache.length - 1)];
                    nextPick = neighbor ? neighbor.sessionId : sessionsCache[0].sessionId;
                }
                confirmingDeleteSessionId = null;
                activeSessionId = nextPick;
                renderSessionList(sessionsCache, activeSessionId);
                status.textContent = 'Session deleted.';
            })
            .catch(function (e) { status.textContent = e.message || 'Delete failed'; });
    }

    /** PUTs a new title for the session, updates cache + row in place. */
    function applySessionRename(sessionId, proposedTitle) {
        if (!sessionId) return Promise.resolve();
        var nextTitle = String(proposedTitle || '').trim();
        if (!nextTitle) {
            status.textContent = 'Session name cannot be empty.';
            return Promise.resolve();
        }
        var current = '';
        for (var i = 0; i < sessionsCache.length; i++) {
            if (sessionsCache[i].sessionId === sessionId) {
                current = sessionsCache[i].title || '';
                break;
            }
        }
        if (nextTitle === current) {
            cancelSessionRename();
            return Promise.resolve();
        }
        return fetch('/api/chat/sessions/' + encodeURIComponent(sessionId), {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: nextTitle })
        })
            .then(function (r) {
                if (!r.ok) throw new Error('Could not rename session');
                return r.json();
            })
            .then(function (updated) {
                for (var j = 0; j < sessionsCache.length; j++) {
                    if (sessionsCache[j].sessionId === sessionId) {
                        sessionsCache[j].title = updated.title || nextTitle;
                        break;
                    }
                }
                renamingSessionId = null;
                renderSessionList(sessionsCache, activeSessionId);
                status.textContent = 'Session renamed.';
            })
            .catch(function (e) { status.textContent = e.message || 'Rename failed'; });
    }

    function loadSessionsForProject(preferredSessionId) {
        if (!activeProjectId) {
            renderSessionList([], null);
            return Promise.resolve();
        }
        return fetch('/api/chat/projects/' + encodeURIComponent(activeProjectId) + '/sessions?limit=100')
            .then(function (r) {
                if (!r.ok) return Promise.reject(new Error('sessions'));
                return r.json();
            })
            .then(function (sessions) {
                return renderSessionList(sessions, preferredSessionId);
            });
    }

    function createProject() {
        var name = String(projectNameInput.value || '').trim();
        if (!name) {
            status.textContent = 'Enter a project name.';
            return Promise.reject(new Error('missing name'));
        }
        var scenarioId = String(scenarioNewSel.value || 'people').trim();
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
                activeProjectScenarioId = String(created.scenarioId || scenarioId).trim();
                return loadProjects(activeProjectId);
            })
            .then(function () {
                return loadSessionsForProject(null);
            });
    }

    function createSession() {
        if (!activeProjectId) {
            status.textContent = 'Select a project first.';
            return Promise.reject(new Error('no project'));
        }
        return fetch('/api/chat/sessions', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ title: 'PM Playground', projectId: activeProjectId })
        })
            .then(function (r) {
                if (!r.ok) throw new Error('Could not create session');
                return r.json();
            })
            .then(function (created) {
                activeSessionId = created.sessionId;
                return loadSessionsForProject(activeSessionId);
            });
    }

    function applyScenarioChange() {
        if (!activeProjectId || !scenarioChangeSel) return Promise.resolve();
        var nextId = String(scenarioChangeSel.value || '').trim();
        if (!nextId || nextId === activeProjectScenarioId) {
            setScenarioPanelVisible(false);
            return Promise.resolve();
        }
        return fetch('/api/chat/projects/' + encodeURIComponent(activeProjectId), {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ scenarioId: nextId })
        })
            .then(function (r) {
                if (!r.ok) throw new Error('Could not update scenario');
                return r.json();
            })
            .then(function (updated) {
                activeProjectScenarioId = String(updated.scenarioId || nextId).trim();
                for (var i = 0; i < projectsCache.length; i++) {
                    if (projectsCache[i].projectId === activeProjectId) {
                        projectsCache[i].scenarioId = activeProjectScenarioId;
                        break;
                    }
                }
                // Scenario change resets the agent override (new scenario may not include the old one).
                agentOverrideId = '';
                renderProjectList();
                updateProjectHeader();
                refreshAgentSelection();
                setScenarioPanelVisible(false);
                status.textContent = 'Scenario updated for this project.';
                return loadFocusEntityOptions();
            });
    }

    /** Submits a new name for the given project; refreshes rail + header in place. */
    function applyRename(projectId, proposedName) {
        if (!projectId) return Promise.resolve();
        var nextName = String(proposedName || '').trim();
        if (!nextName) {
            status.textContent = 'Project name cannot be empty.';
            return Promise.resolve();
        }
        var current = '';
        for (var i = 0; i < projectsCache.length; i++) {
            if (projectsCache[i].projectId === projectId) {
                current = projectsCache[i].name || '';
                break;
            }
        }
        if (nextName === current) {
            cancelRename();
            return Promise.resolve();
        }
        return fetch('/api/chat/projects/' + encodeURIComponent(projectId), {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: nextName })
        })
            .then(function (r) {
                if (!r.ok) throw new Error('Could not rename project');
                return r.json();
            })
            .then(function (updated) {
                for (var j = 0; j < projectsCache.length; j++) {
                    if (projectsCache[j].projectId === projectId) {
                        projectsCache[j].name = updated.name || nextName;
                        break;
                    }
                }
                renamingProjectId = null;
                renderProjectList();
                updateProjectHeader();
                status.textContent = 'Project renamed.';
            })
            .catch(function (e) { status.textContent = e.message || 'Rename failed'; });
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
                        try {
                            events.push(JSON.parse(j));
                        } catch (e2) {
                            /* ignore */
                        }
                    }
                }
            });
        }
        return { rest: rest, events: events };
    }

    function resetFlowViz() {
        if (!flowStepsEl) return;
        flowStepsEl.innerHTML = '';
        if (flowDetailEl) flowDetailEl.textContent = '';
    }

    function appendFlowPlanSteps(plan) {
        if (!flowStepsEl || !plan || !Array.isArray(plan.steps) || plan.steps.length === 0) return;
        plan.steps.forEach(function (s) {
            var arr = document.createElement('span');
            arr.className = 'text-gray-400 dark:text-gray-500 text-[10px] px-0.5 select-none';
            arr.setAttribute('aria-hidden', 'true');
            arr.textContent = '→';
            flowStepsEl.appendChild(arr);
            var d = document.createElement('div');
            d.className =
                'pm-flow-node pm-flow-pending flex max-w-[220px] items-center gap-1.5 rounded border border-gray-300 bg-gray-50 px-2 py-1 text-[10px] text-gray-800 dark:border-gray-600 dark:bg-gray-900 dark:text-gray-100';
            d.setAttribute('data-flow-step', s.id);
            d.innerHTML =
                '<span class="pm-flow-dot inline-block h-2 w-2 shrink-0 rounded-full"></span><span class="leading-tight">' +
                esc(s.label) +
                '</span>';
            if (s.optional && s.active === false) {
                d.classList.add('opacity-40');
            }
            flowStepsEl.appendChild(d);
        });
    }

    function applyFlowPlan(plan) {
        if (!flowStepsEl || !plan || !Array.isArray(plan.steps)) return;
        resetFlowViz();
        plan.steps.forEach(function (s, idx) {
            if (idx > 0) {
                var arr = document.createElement('span');
                arr.className = 'text-gray-400 dark:text-gray-500 text-[10px] px-0.5 select-none';
                arr.setAttribute('aria-hidden', 'true');
                arr.textContent = '→';
                flowStepsEl.appendChild(arr);
            }
            var d = document.createElement('div');
            d.className =
                'pm-flow-node pm-flow-pending flex max-w-[220px] items-center gap-1.5 rounded border border-gray-300 bg-gray-50 px-2 py-1 text-[10px] text-gray-800 dark:border-gray-600 dark:bg-gray-900 dark:text-gray-100';
            d.setAttribute('data-flow-step', s.id);
            d.innerHTML =
                '<span class="pm-flow-dot inline-block h-2 w-2 shrink-0 rounded-full"></span><span class="leading-tight">' +
                esc(s.label) +
                '</span>';
            if (s.optional && s.active === false) {
                d.classList.add('opacity-40');
            }
            flowStepsEl.appendChild(d);
        });
    }

    function setFlowStepStatus(stepId, st, detail) {
        if (!flowStepsEl) return;
        var n = flowStepsEl.querySelector('[data-flow-step="' + stepId + '"]');
        if (!n) return;
        n.classList.remove('pm-flow-pending', 'pm-flow-running', 'pm-flow-done', 'pm-flow-skipped', 'pm-flow-error');
        n.classList.add('pm-flow-' + (st || 'pending'));
        if (flowDetailEl && detail != null && detail !== '') {
            flowDetailEl.textContent = String(detail);
        }
    }

    function sendStreaming() {
        // Effective agent = override (if still in the scoped list) else scenario default.
        var agentId = agentSel && agentSel.value ? agentSel.value : defaultAgentIdForActive();
        var sessionId = activeSessionId;
        var text = input.value.trim();
        if (!agentId) {
            status.textContent = 'No agent available for this scenario.';
            return;
        }
        if (!sessionId) {
            status.textContent = 'Pick or create a session.';
            return;
        }
        if (!text) return;

        sendBtn.disabled = true;
        status.textContent = 'Streaming…';
        resetFlowViz();
        if (flowDetailEl) flowDetailEl.textContent = 'Connecting…';
        if (window.agctorTraceTimeline) {
            window.agctorTraceTimeline.clear(
                'pm-play-trace-timeline',
                'Processing request…',
                'Latest playground request'
            );
        }

        messages.insertAdjacentHTML(
            'beforeend',
            '<div class="rounded-lg border border-gray-200 bg-white dark:border-gray-600 dark:bg-gray-800 p-3">' +
                '<div class="text-xs font-semibold text-gray-500 dark:text-gray-400 mb-1">You</div>' +
                '<div class="whitespace-pre-wrap break-words text-gray-800 dark:text-gray-100">' +
                esc(text) +
                '</div></div>'
        );

        var streamElId = 'pm-stream-' + Date.now();
        var bubbleHtml =
            '<div id="' +
            streamElId +
            '" class="rounded-lg border border-green-200 bg-green-50 dark:border-green-900 dark:bg-green-900/20 p-3">' +
            '<div class="text-xs font-semibold text-green-800 dark:text-green-200 mb-1">Assistant</div>' +
            '<div class="pm-play-md text-sm text-green-900 dark:text-green-100" data-role="stream-body"></div></div>';
        messages.insertAdjacentHTML('beforeend', bubbleHtml);
        messages.scrollTop = messages.scrollHeight;

        var streamBody = document.querySelector('#' + streamElId + ' [data-role="stream-body"]');
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

        var postBody = { sessionId: sessionId, agentId: agentId, payload: text };
        if (activeProjectId && activeProjectScenarioId) {
            postBody.scenarioId = activeProjectScenarioId;
        }

        fetch('/api/project-memory/playground/message/stream', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
            body: JSON.stringify(postBody)
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
                            if (evt.type === 'flow_plan' && evt.payload && flowStepsEl) {
                                try {
                                    applyFlowPlan(JSON.parse(evt.payload));
                                } catch (ePlan) {
                                    /* ignore */
                                }
                            }
                            if (evt.type === 'flow_plan_tail' && evt.payload && flowStepsEl) {
                                try {
                                    appendFlowPlanSteps(JSON.parse(evt.payload));
                                } catch (eTail) {
                                    /* ignore */
                                }
                            }
                            if (evt.type === 'flow_step' && evt.payload) {
                                try {
                                    var fs = JSON.parse(evt.payload);
                                    setFlowStepStatus(fs.id, fs.status, fs.detail);
                                } catch (eStep) {
                                    /* ignore */
                                }
                            }
                            if (evt.type === 'done' && window.agctorTraceTimeline) {
                                var tid =
                                    evt.traceId || evt.TraceId || evt.traceID || evt.trace_id;
                                if (tid) {
                                    window.agctorTraceTimeline.load('pm-play-trace-timeline', tid, {
                                        selectionLabel: 'Latest playground request',
                                        emptyMessage: 'No timeline is available for this request.',
                                        errorMessage: 'Trace timeline is unavailable for this request.'
                                    });
                                }
                            }
                            if (evt.type === 'assistant_tail' && evt.payload) {
                                acc += evt.payload;
                                queueMd();
                            }
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
                loadLifeSignals();
                loadInbox();
                return loadTranscript(sessionId);
            })
            .catch(function (e) {
                status.textContent = 'Error';
                if (flowDetailEl) flowDetailEl.textContent = e.message || String(e);
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
            .finally(function () { newBtn.disabled = !activeProjectId; });
    });

    refreshBtn.addEventListener('click', function () {
        loadSessionsForProject(activeSessionId).catch(function () { status.textContent = 'Refresh failed'; });
    });

    newProjectBtn.addEventListener('click', function () {
        newProjectBtn.disabled = true;
        createProject()
            .catch(function (e) {
                if (e && e.message !== 'missing name') status.textContent = e.message || 'Create project failed';
            })
            .finally(function () { newProjectBtn.disabled = false; });
    });

    if (changeScenarioBtn) {
        changeScenarioBtn.addEventListener('click', function () {
            if (!activeProjectId) return;
            if (scenarioChangeSel && activeProjectScenarioId) scenarioChangeSel.value = activeProjectScenarioId;
            setScenarioPanelVisible(true);
        });
    }
    if (scenarioCancelBtn) {
        scenarioCancelBtn.addEventListener('click', function () {
            setScenarioPanelVisible(false);
        });
    }
    if (scenarioApplyBtn) {
        scenarioApplyBtn.addEventListener('click', function () {
            scenarioApplyBtn.disabled = true;
            applyScenarioChange()
                .catch(function (e) { status.textContent = e.message || 'Update failed'; })
                .finally(function () { scenarioApplyBtn.disabled = false; });
        });
    }

    var nudgesPanelEl = document.getElementById('pm-play-daily-life');
    var lifeSignalsEl = document.getElementById('pm-play-life-signals');
    var nudgesScenarioLabel = document.getElementById('pm-play-nudges-scenario');
    var refreshSignalsBtn = document.getElementById('pm-play-refresh-signals');

    /** Read-only reminders from PersonLifeSignalsReader; chat goes through scenario flow only. */
    function syncNudgesPanel() {
        if (!nudgesPanelEl) return;
        var show = !!activeProjectScenarioId;
        nudgesPanelEl.classList.toggle('hidden', !show);
        if (nudgesScenarioLabel) {
            nudgesScenarioLabel.textContent = show ? '(' + activeProjectScenarioId + ')' : '';
        }
        if (show) loadLifeSignals();
    }

    function loadLifeSignals() {
        if (!lifeSignalsEl || !activeProjectScenarioId) return;
        lifeSignalsEl.innerHTML = '<li>Loading…</li>';
        fetch(
            '/api/project-memory/life-signals?scenarioId=' + encodeURIComponent(activeProjectScenarioId)
        )
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('signals')); })
            .then(function (data) {
                var list = (data && data.signals) || [];
                if (!list.length) {
                    lifeSignalsEl.innerHTML =
                        '<li class="list-none -ml-4 text-emerald-800/70">No urgent nudges in the next 14 days.</li>';
                    return;
                }
                lifeSignalsEl.innerHTML = list
                    .map(function (s) {
                        return '<li>' + esc(s.message || '') + '</li>';
                    })
                    .join('');
            })
            .catch(function () {
                lifeSignalsEl.innerHTML = '<li class="list-none -ml-4">Could not load nudges.</li>';
            });
    }

    if (refreshSignalsBtn) {
        refreshSignalsBtn.addEventListener('click', loadLifeSignals);
    }

    var inboxPanelEl = document.getElementById('pm-play-inbox');
    var inboxListEl = document.getElementById('pm-play-inbox-list');
    var inboxCountEl = document.getElementById('pm-play-inbox-count');
    var refreshInboxBtn = document.getElementById('pm-play-refresh-inbox');
    var privacyPanelEl = document.getElementById('pm-play-privacy');
    var privacyAutoIngestEl = document.getElementById('pm-play-privacy-auto-ingest');
    var forgetEntitySel = document.getElementById('pm-play-forget-entity');
    var forgetBtn = document.getElementById('pm-play-forget-btn');
    var exportBtn = document.getElementById('pm-play-export-btn');

    function syncInboxPanel() {
        if (!inboxPanelEl) return;
        var show = !!activeProjectScenarioId;
        inboxPanelEl.classList.toggle('hidden', !show);
        if (show) loadInbox();
    }

    function loadInbox() {
        if (!inboxListEl || !activeProjectScenarioId) return;
        inboxListEl.innerHTML = '<li>Loading…</li>';
        fetch(
            '/api/project-memory/generic-inbox/pending?scenarioId=' + encodeURIComponent(activeProjectScenarioId)
        )
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('inbox')); })
            .then(function (data) {
                var items = (data && data.items) || [];
                if (inboxCountEl) {
                    inboxCountEl.textContent = items.length ? '(' + items.length + ' pending)' : '';
                }
                if (!items.length) {
                    inboxListEl.innerHTML =
                        '<li class="list-none text-amber-900/70">Nothing waiting for review.</li>';
                    return;
                }
                inboxListEl.innerHTML = items
                    .map(function (item) {
                        var line = esc(item.userPromptLine || item.value || '');
                        var meta = esc(item.entityKey || '') + ' · ' + esc(item.knowledgeType || '');
                        return (
                            '<li class="border border-amber-200/80 rounded p-2 dark:border-amber-800">' +
                            '<div class="font-medium">' + line + '</div>' +
                            '<div class="text-[10px] opacity-80 mt-0.5">' + meta + '</div>' +
                            '<div class="mt-1.5 flex gap-2">' +
                            '<button type="button" class="pm-inbox-approve text-[10px] font-medium text-emerald-800 hover:underline" data-id="' +
                            esc(item.proposalId) +
                            '">Approve</button>' +
                            '<button type="button" class="pm-inbox-reject text-[10px] font-medium text-red-800 hover:underline" data-id="' +
                            esc(item.proposalId) +
                            '">Reject</button>' +
                            '</div></li>'
                        );
                    })
                    .join('');
                inboxListEl.querySelectorAll('.pm-inbox-approve').forEach(function (btn) {
                    btn.addEventListener('click', function () {
                        decideInbox(btn.getAttribute('data-id'), true);
                    });
                });
                inboxListEl.querySelectorAll('.pm-inbox-reject').forEach(function (btn) {
                    btn.addEventListener('click', function () {
                        decideInbox(btn.getAttribute('data-id'), false);
                    });
                });
            })
            .catch(function () {
                inboxListEl.innerHTML = '<li class="list-none">Could not load inbox.</li>';
            });
    }

    function decideInbox(proposalId, approve) {
        if (!proposalId || !activeProjectScenarioId) return;
        fetch('/api/project-memory/generic-inbox/decide', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                scenarioId: activeProjectScenarioId,
                decisions: [{ proposalId: proposalId, approve: approve }]
            })
        })
            .then(function (r) { return r.ok ? r.json() : Promise.reject(new Error('decide')); })
            .then(function () {
                loadInbox();
                loadLifeSignals();
            })
            .catch(function () {
                status.textContent = 'Inbox action failed';
            });
    }

    if (refreshInboxBtn) refreshInboxBtn.addEventListener('click', loadInbox);

    function syncPrivacyPanel() {
        if (!privacyPanelEl) return;
        var show = !!activeProjectScenarioId;
        privacyPanelEl.classList.toggle('hidden', !show);
        if (!show) return;
        fetch('/api/project-memory/privacy/settings')
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (s) {
                if (privacyAutoIngestEl && s) {
                    privacyAutoIngestEl.checked = s.autoIngestOnSessionEnd !== false;
                }
            })
            .catch(function () { /* defaults */ });
        populateForgetEntityOptions();
    }

    function populateForgetEntityOptions() {
        if (!forgetEntitySel || !activeProjectScenarioId) return;
        fetch('/api/project-memory/scenario-entities?scenarioId=' + encodeURIComponent(activeProjectScenarioId))
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (entities) {
                forgetEntitySel.innerHTML = '';
                (entities || []).forEach(function (e) {
                    var opt = document.createElement('option');
                    opt.value = e.entityKey || '';
                    opt.textContent = (e.displayName || e.entityKey) + ' (' + e.entityKey + ')';
                    forgetEntitySel.appendChild(opt);
                });
            })
            .catch(function () { /* ignore */ });
    }

    function savePrivacySettings() {
        if (!privacyAutoIngestEl) return;
        fetch('/api/project-memory/privacy/settings', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ autoIngestOnSessionEnd: !!privacyAutoIngestEl.checked })
        }).catch(function () { status.textContent = 'Could not save privacy settings'; });
    }

    if (privacyAutoIngestEl) {
        privacyAutoIngestEl.addEventListener('change', savePrivacySettings);
    }

    if (forgetBtn) {
        forgetBtn.addEventListener('click', function () {
            var key = forgetEntitySel && forgetEntitySel.value ? forgetEntitySel.value.trim() : '';
            if (!key || !activeProjectScenarioId) return;
            var label = forgetEntitySel.options[forgetEntitySel.selectedIndex]
                ? forgetEntitySel.options[forgetEntitySel.selectedIndex].textContent
                : key;
            if (!window.confirm('Permanently delete all memory files for ' + label + '?')) return;
            fetch('/api/project-memory/privacy/forget-person', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    scenarioId: activeProjectScenarioId,
                    entityKey: key,
                    projectId: activeProjectId || null,
                    clearProjectFocusWhenMatched: true
                })
            })
                .then(function (r) {
                    if (!r.ok) throw new Error('forget failed');
                    status.textContent = 'Removed ' + key;
                    loadFocusEntityOptions();
                    populateForgetEntityOptions();
                    loadLifeSignals();
                    loadInbox();
                })
                .catch(function () {
                    status.textContent = 'Forget person failed';
                });
        });
    }

    if (exportBtn) {
        exportBtn.addEventListener('click', function () {
            if (!activeProjectScenarioId) return;
            window.location.href =
                '/api/project-memory/privacy/export?scenarioId=' +
                encodeURIComponent(activeProjectScenarioId);
        });
    }

    function syncCompanionPanels() {
        syncNudgesPanel();
        syncInboxPanel();
        syncPrivacyPanel();
    }

    var focusEntitySel = document.getElementById('pm-play-focus-entity');

    function activeProject() {
        if (!activeProjectId) return null;
        for (var i = 0; i < projectsCache.length; i++) {
            if (projectsCache[i].projectId === activeProjectId) return projectsCache[i];
        }
        return null;
    }

    function loadFocusEntityOptions() {
        if (!focusEntitySel || !activeProjectScenarioId) return Promise.resolve();
        return fetch('/api/project-memory/scenario-entities?scenarioId=' + encodeURIComponent(activeProjectScenarioId))
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (entities) {
                var current = activeProject();
                var selected = current && current.focusEntityKey ? current.focusEntityKey : '';
                focusEntitySel.innerHTML = '<option value="">(none — infer from chat)</option>';
                (entities || []).forEach(function (e) {
                    var opt = document.createElement('option');
                    opt.value = e.entityKey || '';
                    opt.textContent = (e.displayName || e.entityKey) + ' (' + e.entityKey + ')';
                    opt.dataset.display = e.displayName || e.entityKey;
                    focusEntitySel.appendChild(opt);
                });
                focusEntitySel.value = selected;
            })
            .catch(function () {
                /* keep existing options */
            });
    }

    function applyFocusEntityChange() {
        if (!activeProjectId || !focusEntitySel) return Promise.resolve();
        var key = String(focusEntitySel.value || '').trim();
        var opt = focusEntitySel.options[focusEntitySel.selectedIndex];
        var display = opt && opt.dataset.display ? opt.dataset.display : key;
        return fetch('/api/chat/projects/' + encodeURIComponent(activeProjectId), {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                focusEntityKey: key,
                focusDisplayName: key ? display : ''
            })
        })
            .then(function (r) {
                if (!r.ok) throw new Error('Could not update focus person');
                return r.json();
            })
            .then(function (updated) {
                for (var i = 0; i < projectsCache.length; i++) {
                    if (projectsCache[i].projectId === activeProjectId) {
                        projectsCache[i].focusEntityKey = updated.focusEntityKey || null;
                        projectsCache[i].focusDisplayName = updated.focusDisplayName || null;
                        break;
                    }
                }
                status.textContent = key ? 'Focus person set.' : 'Focus cleared.';
            })
            .catch(function (e) {
                status.textContent = e.message || 'Focus update failed';
            });
    }

    if (focusEntitySel) {
        focusEntitySel.addEventListener('change', applyFocusEntityChange);
    }

    var _updateProjectHeaderOrig = updateProjectHeader;
    updateProjectHeader = function () {
        _updateProjectHeaderOrig();
        syncCompanionPanels();
        loadFocusEntityOptions();
    };

    sendBtn.addEventListener('click', sendStreaming);
    input.addEventListener('keydown', function (ev) {
        if (ev.key === 'Enter' && !ev.shiftKey) {
            ev.preventDefault();
            sendStreaming();
        }
    });

    // Changing the Advanced agent select promotes the pick to an override.
    agentSel.addEventListener('change', function () {
        agentOverrideId = agentSel.value || '';
        refreshAgentSelection();
    });

    if (agentResetBtn) {
        agentResetBtn.addEventListener('click', function () {
            agentOverrideId = '';
            refreshAgentSelection();
            status.textContent = 'Agent reset to scenario default.';
        });
    }

    var qs = new URLSearchParams(window.location.search);
    var qProject = qs.get('projectId');
    var qSession = qs.get('sessionId');
    /** True when ?sessionId= loaded and that session has no projectId (orphan transcript path). */
    var sessionNotBoundToProject = false;

    loadAgents()
        .then(loadScenarios)
        .then(function () {
            // Resolve owning project from the session whenever sessionId is present (fixes stale or missing projectId in URL).
            if (qSession) {
                return fetch('/api/chat/sessions/' + encodeURIComponent(qSession))
                    .then(function (r) { return r.ok ? r.json() : null; })
                    .then(function (tr) {
                        sessionNotBoundToProject = false;
                        if (tr && tr.session) {
                            if (tr.session.projectId) {
                                qProject = tr.session.projectId;
                            } else {
                                sessionNotBoundToProject = true;
                                qProject = null;
                            }
                        }
                    });
            }
        })
        .then(function () {
            return loadProjects(qProject || null, { skipDefaultFirst: sessionNotBoundToProject });
        })
        .then(function () {
            // Session pointed at a project that no longer exists — same orphan handling as no projectId.
            if (qSession && qProject && !projectsCache.some(function (x) { return x.projectId === qProject; })) {
                activeProjectId = null;
                activeProjectScenarioId = '';
                agentOverrideId = '';
                renderProjectList();
                updateProjectHeader();
                refreshAgentSelection();
                syncUrl();
            }
            if (activeProjectId) {
                return loadSessionsForProject(qSession || null);
            }
            if (qSession) {
                activeSessionId = qSession;
                syncUrl();
                syncSessionColumn();
                if (hint) {
                    hint.textContent =
                        'This session is not linked to a project (or no project was resolved). Pick a project to manage sessions.';
                }
                return loadTranscript(qSession);
            }
            renderSessionList([], null);
            return Promise.resolve();
        })
        .catch(function (e) {
            status.textContent = e.message || 'Load failed';
            if (hint) hint.textContent = 'Set project root on Maintenance if agents fail to load.';
        });
})();
