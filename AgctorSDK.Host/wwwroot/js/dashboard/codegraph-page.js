/**
 * CodeGraph dashboard: load /api/CodeGraph/current, hydrate ViewComponent DOM ids, chat, index, vectors debug, trace timeline.
 * PRD-007 Phase 4 — keep in sync with Pages/Dashboard/CodeGraph.cshtml markup ids.
 */
(function() {
    const el = document.getElementById('codegraph-content');
    const panels = document.getElementById('codegraph-panels');
    function esc(s) { const d = document.createElement('div'); d.textContent = s ?? ''; return d.innerHTML; }
    function normalizeMarkdownInput(text) {
        if (!text) return '';
        let t = String(text);
        // Help model outputs where list items are produced on one line.
        t = t.replace(/:\s+(\d+\.)\s/g, ':\n$1 ');
        t = t.replace(/\)\s+(\d+\.)\s/g, ')\n$1 ');
        return t;
    }
    function renderInlineMarkdown(text) {
        let html = esc(text || '');
        html = html.replace(/`([^`]+)`/g, '<code>$1</code>');
        html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
        html = html.replace(/\*([^*]+)\*/g, '<em>$1</em>');
        return html;
    }
    function renderBasicMarkdown(text) {
        const source = normalizeMarkdownInput(text);
        const lines = source.split(/\r?\n/);
        let html = '';
        let inOl = false;
        let inUl = false;
        function closeLists() {
            if (inOl) { html += '</ol>'; inOl = false; }
            if (inUl) { html += '</ul>'; inUl = false; }
        }
        lines.forEach(function(rawLine) {
            const line = (rawLine || '').trim();
            if (!line) {
                closeLists();
                return;
            }
            const olMatch = line.match(/^(\d+)\.\s+(.+)$/);
            if (olMatch) {
                if (inUl) { html += '</ul>'; inUl = false; }
                if (!inOl) { html += '<ol>'; inOl = true; }
                html += '<li>' + renderInlineMarkdown(olMatch[2]) + '</li>';
                return;
            }
            const ulMatch = line.match(/^[-*]\s+(.+)$/);
            if (ulMatch) {
                if (inOl) { html += '</ol>'; inOl = false; }
                if (!inUl) { html += '<ul>'; inUl = true; }
                html += '<li>' + renderInlineMarkdown(ulMatch[1]) + '</li>';
                return;
            }
            closeLists();
            html += '<p>' + renderInlineMarkdown(line) + '</p>';
        });
        closeLists();
        return html;
    }
    function renderChatMarkdown(text) {
        const source = normalizeMarkdownInput(text);
        if (window.marked && typeof window.marked.parse === 'function') {
            const rawHtml = window.marked.parse(source, { gfm: true, breaks: true });
            if (window.DOMPurify && typeof window.DOMPurify.sanitize === 'function') {
                return window.DOMPurify.sanitize(rawHtml);
            }
            return rawHtml;
        }
        return renderBasicMarkdown(source);
    }

    function setLoadVectorsButtonState(button, vectorCount) {
        if (!button) return;
        const count = Number(vectorCount || 0);
        const hasVectors = count > 0;
        button.dataset.vectorCount = String(count);
        button.disabled = !hasVectors;
        button.title = hasVectors ? 'Load stored embedding vectors' : 'No vectors in the embedding store yet';
        button.className = hasVectors
            ? 'px-3 py-1.5 text-sm font-medium text-white bg-gray-600 rounded hover:bg-gray-700 dark:bg-gray-600 dark:hover:bg-gray-700'
            : 'px-3 py-1.5 text-sm font-medium text-white bg-gray-400 rounded cursor-not-allowed dark:bg-gray-500';
    }

    fetch('/api/CodeGraph/current')
        .then(r => { if (r.status === 404) return null; return r.ok ? r.json() : Promise.reject(r); })
        .then(ctx => {
            if (!ctx) {
                if (panels) panels.classList.add('hidden');
                el.classList.remove('hidden');
                el.innerHTML = '<div class="p-6 bg-amber-50 border border-amber-200 rounded-lg dark:bg-gray-800 dark:border-amber-800">' +
                    '<h2 class="text-lg font-semibold text-amber-900 dark:text-amber-200">CodeGraph not active</h2>' +
                    '<p class="mt-2 text-amber-800 dark:text-amber-200">Run the <strong>code-graph-demo</strong> scenario to create the actor model and embedding store.</p>' +
                    '<p class="mt-2 text-sm text-amber-700 dark:text-amber-300">Go to the <a href="/Dashboard/Agents" class="underline font-medium hover:no-underline">Agents</a> page, select &quot;code-graph-demo&quot; and click <strong>Apply</strong>.</p>' +
                    '</div>';
                return;
            }
            const summary = ctx.embeddingStoreSummary;
            const vectorCount = summary && (summary.vectorCount !== undefined) ? summary.vectorCount : 0;
            const embeddingState = summary && summary.state ? summary.state : 'NotReady';
            const graphVersion = summary && summary.graphVersion !== undefined ? summary.graphVersion : 0;
            const indexedGraphVersion = summary && summary.indexedGraphVersion !== undefined ? summary.indexedGraphVersion : 0;
            const lastIndexedAt = summary && summary.lastIndexedAt ? summary.lastIndexedAt : '';
            const lastError = summary && summary.lastError ? summary.lastError : '';
            el.classList.add('hidden');
            if (panels) panels.classList.remove('hidden');
            const tree = ctx.actorTree;
            const rawJsonPre = document.getElementById('codegraph-raw-json');
            if (rawJsonPre) rawJsonPre.textContent = JSON.stringify(ctx, null, 2);
            const countEl = document.getElementById('codegraph-vector-count');
            const stateEl = document.getElementById('codegraph-embedding-state');
            const graphVersionEl = document.getElementById('codegraph-graph-version');
            const indexedVersionEl = document.getElementById('codegraph-indexed-graph-version');
            const lastIndexedEl = document.getElementById('codegraph-last-indexed-at');
            const errorEl = document.getElementById('codegraph-embedding-error');
            if (countEl) countEl.textContent = String(vectorCount);
            if (stateEl) stateEl.textContent = String(embeddingState);
            if (graphVersionEl) graphVersionEl.textContent = String(graphVersion);
            if (indexedVersionEl) indexedVersionEl.textContent = String(indexedGraphVersion);
            if (lastIndexedEl) lastIndexedEl.textContent = String(lastIndexedAt || 'Not yet indexed');
            if (errorEl) {
                errorEl.textContent = lastError ? ('Last error: ' + lastError) : '';
                errorEl.className = lastError
                    ? 'mt-1 text-xs text-red-600 dark:text-red-400'
                    : 'mt-1 text-xs text-gray-500 dark:text-gray-400';
            }
            if (window.agctorActorTree) {
                window.agctorActorTree.render('codegraph-actor-tree', tree);
            }
            function ensureTraceTimelineMounted() {
                const host = document.getElementById('codegraph-trace-host');
                const timeline = document.getElementById('codegraph-trace-timeline');
                if (host && timeline && timeline.parentElement !== host) {
                    host.appendChild(timeline);
                }
            }
            ensureTraceTimelineMounted();
            if (window.agctorTraceTimeline) {
                window.agctorTraceTimeline.clear('codegraph-trace-timeline');
            }
            const indexBtn = document.getElementById('index-now-btn');
            const indexMsg = document.getElementById('index-message');
            if (indexBtn && indexMsg) {
                indexBtn.addEventListener('click', async function() {
                    indexBtn.disabled = true;
                    indexMsg.textContent = 'Indexing...';
                    indexMsg.className = 'text-sm text-gray-600 dark:text-gray-400';
                    try {
                        const res = await fetch('/api/agents/embedding-coordinator-agent/message', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ payload: 'index' })
                        });
                        const data = await res.json().catch(() => ({}));
                        if (res.ok) {
                            indexMsg.textContent = 'Done. Refreshing...';
                            indexMsg.className = 'text-sm text-green-600 dark:text-green-400';
                            const updated = await fetch('/api/CodeGraph/current').then(r => r.ok ? r.json() : null);
                            const countEl = document.getElementById('codegraph-vector-count');
                            const stateEl = document.getElementById('codegraph-embedding-state');
                            const graphVersionEl = document.getElementById('codegraph-graph-version');
                            const indexedVersionEl = document.getElementById('codegraph-indexed-graph-version');
                            const lastIndexedEl = document.getElementById('codegraph-last-indexed-at');
                            const errorEl = document.getElementById('codegraph-embedding-error');
                            const updatedCount = updated && updated.embeddingStoreSummary ? (updated.embeddingStoreSummary.vectorCount ?? 0) : 0;
                            if (countEl) countEl.textContent = String(updatedCount);
                            if (stateEl && updated && updated.embeddingStoreSummary) stateEl.textContent = String(updated.embeddingStoreSummary.state ?? 'Unknown');
                            if (graphVersionEl && updated && updated.embeddingStoreSummary) graphVersionEl.textContent = String(updated.embeddingStoreSummary.graphVersion ?? 0);
                            if (indexedVersionEl && updated && updated.embeddingStoreSummary) indexedVersionEl.textContent = String(updated.embeddingStoreSummary.indexedGraphVersion ?? 0);
                            if (lastIndexedEl && updated && updated.embeddingStoreSummary) lastIndexedEl.textContent = String(updated.embeddingStoreSummary.lastIndexedAt ?? 'Not yet indexed');
                            if (errorEl && updated && updated.embeddingStoreSummary) errorEl.textContent = updated.embeddingStoreSummary.lastError ? ('Last error: ' + updated.embeddingStoreSummary.lastError) : '';
                            setLoadVectorsButtonState(document.getElementById('load-vectors-btn'), updatedCount);
                            indexMsg.textContent = 'Indexing complete.';
                        } else {
                            indexMsg.textContent = data.message || 'Indexing failed.';
                            indexMsg.className = 'text-sm text-red-600 dark:text-red-400';
                        }
                    } catch (e) {
                        indexMsg.textContent = 'Error: ' + (e.message || 'Request failed');
                        indexMsg.className = 'text-sm text-red-600 dark:text-red-400';
                    }
                    indexBtn.disabled = false;
                });
            }
            const loadVectorsBtn = document.getElementById('load-vectors-btn');
            const debugResult = document.getElementById('embedding-debug-result');
            setLoadVectorsButtonState(loadVectorsBtn, vectorCount);
            if (loadVectorsBtn && debugResult) {
                loadVectorsBtn.addEventListener('click', async function() {
                    if (loadVectorsBtn.disabled) return;
                    loadVectorsBtn.disabled = true;
                    loadVectorsBtn.textContent = 'Loading...';
                    try {
                        const r = await fetch('/api/CodeGraph/embeddings');
                        if (!r.ok) { debugResult.innerHTML = '<p class="text-sm text-red-600">No embeddings or CodeGraph not active.</p>'; return; }
                        const records = await r.json();
                        if (!records || records.length === 0) {
                            debugResult.innerHTML = '<p class="text-sm text-gray-500">No vectors in store. Click &quot;Index now&quot; first.</p>';
                            return;
                        }
                        const previewLen = 8;
                        let tableHtml = '<div class="overflow-x-auto max-h-64 overflow-y-auto"><table class="text-sm w-full border border-gray-200 dark:border-gray-600"><thead><tr class="bg-gray-100 dark:bg-gray-700"><th class="text-left p-2">Actor ID</th><th class="text-left p-2">Text</th><th class="text-left p-2">Dims</th><th class="text-left p-2">First ' + previewLen + ' values</th></tr></thead><tbody>';
                        records.forEach(rec => {
                            const vec = rec.vector || [];
                            const preview = vec.slice(0, previewLen).map(v => Number(v).toFixed(3)).join(', ');
                            tableHtml += '<tr class="border-t border-gray-200 dark:border-gray-600"><td class="p-2 font-mono text-xs">' + esc(rec.actorId) + '</td><td class="p-2">' + esc(rec.text) + '</td><td class="p-2">' + vec.length + '</td><td class="p-2 font-mono text-xs text-gray-600 dark:text-gray-400">' + esc(preview) + (vec.length > previewLen ? '...' : '') + '</td></tr>';
                        });
                        tableHtml += '</tbody></table></div>';
                        const d0 = records.map(r => (r.vector && r.vector[0]) != null ? Number(r.vector[0]) : 0);
                        const d1 = records.map(r => (r.vector && r.vector[1]) != null ? Number(r.vector[1]) : 0);
                        const min0 = Math.min(...d0), max0 = Math.max(...d0), min1 = Math.min(...d1), max1 = Math.max(...d1);
                        const range0 = max0 - min0 || 1, range1 = max1 - min1 || 1;
                        const w = 400, h = 300, pad = 30;
                        let svg = '<p class="text-xs text-gray-500 dark:text-gray-400 mt-2">2D preview: first dimension (x) vs second dimension (y). For full PCA/t-SNE use external tools (e.g. TensorBoard, Python).</p>';
                        svg += '<svg width="' + w + '" height="' + h + '" class="border border-gray-200 dark:border-gray-600 rounded mt-1" viewBox="0 0 ' + w + ' ' + h + '">';
                        records.forEach((rec, i) => {
                            const x = pad + (d0[i] - min0) / range0 * (w - 2 * pad);
                            const y = h - pad - (d1[i] - min1) / range1 * (h - 2 * pad);
                            svg += '<circle cx="' + x + '" cy="' + y + '" r="4" fill="#3b82f6" opacity="0.8"/><title>' + esc(rec.text) + '</title>';
                        });
                        svg += '</svg>';
                        debugResult.innerHTML = tableHtml + svg;
                    } catch (e) {
                        debugResult.innerHTML = '<p class="text-sm text-red-600">Error: ' + esc(e.message || 'Request failed') + '</p>';
                    }
                    loadVectorsBtn.textContent = 'Load vectors';
                    setLoadVectorsButtonState(loadVectorsBtn, loadVectorsBtn.dataset.vectorCount || 0);
                });
            }
            const chatSend = document.getElementById('codegraph-chat-send');
            const chatInput = document.getElementById('codegraph-chat-input');
            const chatAgent = document.getElementById('codegraph-chat-agent');
            const chatSession = document.getElementById('codegraph-chat-session');
            const chatNewSession = document.getElementById('codegraph-chat-new-session');
            const chatSessionLabel = document.getElementById('codegraph-chat-session-label');
            const chatHelp = document.getElementById('codegraph-chat-help');
            const chatMessages = document.getElementById('codegraph-chat-messages');
            if (chatSend && chatInput && chatAgent && chatHelp && chatMessages && chatSession && chatNewSession) {
                let activeSessionId = null;
                let selectedTraceTarget = '';

                function sessionLabelText(sessionId) {
                    if (!sessionId) return 'No session selected';
                    return 'Active session: ' + sessionId;
                }

                function setSessionLabel(sessionId) {
                    if (chatSessionLabel) chatSessionLabel.textContent = sessionLabelText(sessionId);
                }

                function updateSelectedTraceState(target) {
                    selectedTraceTarget = target || '';
                    chatMessages.querySelectorAll('[data-chat-trace-target]').forEach(function(node) {
                        const isSelected = selectedTraceTarget && node.dataset.chatTraceTarget === selectedTraceTarget;
                        node.classList.toggle('ring-2', !!isSelected);
                        node.classList.toggle('ring-violet-500', !!isSelected);
                        node.classList.toggle('border-violet-400', !!isSelected);
                    });
                }

                function normalizeRole(roleRaw) {
                    return typeof roleRaw === 'number'
                        ? (roleRaw === 0 ? 'user' : roleRaw === 1 ? 'assistant' : roleRaw === 2 ? 'system' : 'tool')
                        : String(roleRaw || '').toLowerCase();
                }

                function roleLabel(role, turn) {
                    if (role === 'user') return 'You';
                    return turn && turn.agentId ? String(turn.agentId) : 'assistant';
                }

                function resolveTraceLink(traceLinks, turnId) {
                    return (traceLinks || []).find(function(link) {
                        return link.requestTurnId === turnId || link.responseTurnId === turnId;
                    }) || null;
                }

                function groupTranscriptTurns(turns, traceLinks) {
                    const groups = [];
                    let current = null;
                    (turns || []).forEach(function(turn) {
                        const turnGroupId = turn.turnGroupId || turn.turnId;
                        const traceLink = resolveTraceLink(traceLinks, turn.turnId);
                        if (!current || current.turnGroupId !== turnGroupId) {
                            current = {
                                turnGroupId: turnGroupId,
                                turnTraceId: traceLink ? (traceLink.primaryTraceId || traceLink.responseTraceId || traceLink.requestTraceId || '') : '',
                                turns: []
                            };
                            groups.push(current);
                        }

                        current.turns.push({
                            turn: turn,
                            traceLink: traceLink
                        });
                    });

                    return groups;
                }

                function buildTraceButton(label, traceId, target, selectionLabel, extraClass) {
                    if (!traceId) return '';
                    return '<button type="button" class="' + (extraClass || 'px-2 py-1 rounded border border-violet-200 text-violet-700 dark:text-violet-300 dark:border-violet-700 text-xs hover:bg-violet-50 dark:hover:bg-violet-900/30') + '"' +
                        ' data-trace-id="' + esc(traceId) + '"' +
                        ' data-trace-target="' + esc(target) + '"' +
                        ' data-selection-label="' + esc(selectionLabel) + '">' + esc(label) + '</button>';
                }

                async function createSession() {
                    const response = await fetch('/api/chat/sessions', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({})
                    });
                    if (!response.ok) throw new Error('Failed to create session');
                    return await response.json();
                }

                async function loadSessionTranscript(sessionId, preferredTraceTarget, preferredTraceId) {
                    if (!sessionId) return;
                    const response = await fetch('/api/chat/sessions/' + encodeURIComponent(sessionId));
                    if (!response.ok) {
                        chatMessages.innerHTML = '<div class="text-red-600 dark:text-red-400">Failed to load session transcript.</div>';
                        return;
                    }
                    const transcript = await response.json();
                    const turns = transcript && transcript.turns ? transcript.turns : [];
                    const traceLinks = transcript && transcript.traceLinks ? transcript.traceLinks : [];
                    if (!Array.isArray(turns) || turns.length === 0) {
                        chatMessages.innerHTML = '<div class="text-gray-500 dark:text-gray-400">No messages yet in this session.</div>';
                        updateSelectedTraceState('');
                        if (window.agctorTraceTimeline) {
                            ensureTraceTimelineMounted();
                            window.agctorTraceTimeline.clear('codegraph-trace-timeline', 'Select a prompt or response to visualize its trace.', 'No trace selected.');
                        }
                        return;
                    }

                    let html = '';
                    groupTranscriptTurns(turns, traceLinks).forEach(function(group, groupIndex) {
                        const turnTarget = 'turn:' + group.turnGroupId;
                        const turnTraceId = group.turnTraceId || '';
                        html += '<div class="rounded-xl border border-gray-200 dark:border-gray-700 p-3 bg-gray-50/70 dark:bg-gray-900/20 space-y-2 transition" data-chat-trace-target="' + esc(turnTarget) + '">' +
                            '<div class="flex flex-wrap items-center justify-between gap-2">' +
                            '<div class="text-xs font-medium uppercase tracking-wide text-gray-500 dark:text-gray-400">Turn ' + esc(String(groupIndex + 1)) + '</div>' +
                            '<div class="flex flex-wrap items-center gap-1">' +
                            (turnTraceId ? buildTraceButton('Turn trace', turnTraceId, turnTarget, 'Turn trace • interaction ' + String(groupIndex + 1)) : '') +
                            (!turnTraceId ? '<span class="px-2 py-1 rounded border border-gray-200 text-gray-500 dark:border-gray-600 dark:text-gray-400 text-xs">No trace</span>' : '') +
                            '</div>' +
                            '</div>';

                        group.turns.forEach(function(entry) {
                            const turn = entry.turn;
                            const traceLink = entry.traceLink;
                            const role = normalizeRole(turn.role);
                            const content = turn.content || '';
                            const messageTraceId = traceLink
                                ? ((traceLink.requestTurnId === turn.turnId
                                    ? (traceLink.requestTraceId || traceLink.primaryTraceId || '')
                                    : (traceLink.responseTraceId || traceLink.primaryTraceId || '')))
                                : '';
                            const hasTrace = !!(messageTraceId || turnTraceId);
                            const messageTarget = 'message:' + turn.turnId;
                            const selectionLabel = (role === 'user' ? 'Request trace' : 'Response trace') + ' • ' + roleLabel(role, turn);
                            const bubbleClass = role === 'user'
                                ? 'border-gray-200 bg-white dark:border-gray-600 dark:bg-gray-800 text-gray-700 dark:text-gray-200'
                                : 'border-green-200 bg-green-50 dark:border-green-900 dark:bg-green-900/20 text-green-800 dark:text-green-200';
                            html += '<div class="rounded-lg border p-3 transition ' + bubbleClass + (hasTrace ? ' cursor-pointer hover:border-violet-400 dark:hover:border-violet-500' : '') + '"' +
                                (hasTrace ? ' data-trace-id="' + esc(messageTraceId || turnTraceId) + '"' : '') +
                                (hasTrace ? ' data-selection-label="' + esc(selectionLabel) + '"' : '') +
                                (hasTrace ? ' data-trace-target="' + esc(messageTarget) + '"' : '') +
                                ' data-chat-trace-target="' + esc(messageTarget) + '">' +
                                '<div class="flex flex-wrap items-center justify-between gap-2">' +
                                '<strong>' + esc(roleLabel(role, turn)) + '</strong>' +
                                '<div class="flex flex-wrap items-center gap-1">' +
                                (messageTraceId && messageTraceId !== turnTraceId ? buildTraceButton('Message trace', messageTraceId, messageTarget, selectionLabel) : '') +
                                (!hasTrace ? '<span class="px-2 py-1 rounded border border-gray-200 text-gray-500 dark:border-gray-600 dark:text-gray-400 text-xs">No trace</span>' : '') +
                                '</div>' +
                                '</div>' +
                                '<div class="mt-2">' + (role === 'user' ? esc(content) : '<span class="codegraph-chat-markdown">' + renderChatMarkdown(content) + '</span>') + '</div>' +
                                '</div>';
                        });

                        html += '</div>';
                    });
                    chatMessages.innerHTML = html;
                    chatMessages.scrollTop = chatMessages.scrollHeight;

                    updateSelectedTraceState('');
                    if (preferredTraceTarget) {
                        const targetButton = chatMessages.querySelector('[data-trace-target="' + preferredTraceTarget.replace(/"/g, '\\"') + '"]');
                        if (targetButton) {
                            targetButton.click();
                            return;
                        }
                    }

                    if (preferredTraceId) {
                        const traceButton = chatMessages.querySelector('[data-trace-id="' + preferredTraceId.replace(/"/g, '\\"') + '"]');
                        if (traceButton) {
                            traceButton.click();
                        }
                    }
                }

                async function refreshSessions(preferredSessionId) {
                    const response = await fetch('/api/chat/sessions?limit=100');
                    if (!response.ok) throw new Error('Failed to load sessions');
                    const sessions = await response.json();
                    chatSession.innerHTML = '';
                    (sessions || []).forEach(function(session) {
                        const option = document.createElement('option');
                        option.value = session.sessionId;
                        option.textContent = (session.title || session.sessionId) + ' (' + session.turnCount + ')';
                        chatSession.appendChild(option);
                    });

                    if (!sessions || sessions.length === 0) {
                        const created = await createSession();
                        await refreshSessions(created.sessionId);
                        return;
                    }

                    const selected = preferredSessionId || activeSessionId || sessions[0].sessionId;
                    chatSession.value = selected;
                    activeSessionId = selected;
                    setSessionLabel(activeSessionId);
                    await loadSessionTranscript(activeSessionId);
                }

                function syncChatAgentUi() {
                    const agentId = chatAgent.value;
                    if (agentId === 'query-agent') {
                        chatHelp.textContent = 'query-agent answers questions about indexed code. Use coder-agent to write or edit code, and refactor-agent for refactors. Click Index now before asking code questions.';
                        chatInput.placeholder = 'e.g. Where is Square defined?';
                        return;
                    }

                    if (agentId === 'coder-agent') {
                        chatHelp.textContent = 'coder-agent is for creating or editing code in the demo workspace.';
                        chatInput.placeholder = 'e.g. Create a new file named Geometry.cs with a Triangle class';
                        return;
                    }

                    chatHelp.textContent = 'refactor-agent is for code cleanups and structural changes to existing code.';
                    chatInput.placeholder = 'e.g. Refactor Calculator to extract division validation';
                }

                syncChatAgentUi();
                chatAgent.addEventListener('change', syncChatAgentUi);
                chatMessages.addEventListener('click', function(event) {
                    const traceTrigger = event.target.closest('[data-trace-id]');
                    if (!traceTrigger) return;
                    const traceId = traceTrigger.dataset.traceId;
                    const traceTarget = traceTrigger.dataset.traceTarget || traceTrigger.dataset.chatTraceTarget || '';
                    const selectionLabel = traceTrigger.dataset.selectionLabel || 'Selected trace';
                    updateSelectedTraceState(traceTarget);
                    if (window.agctorTraceTimeline) {
                        ensureTraceTimelineMounted();
                        window.agctorTraceTimeline.load('codegraph-trace-timeline', traceId, {
                            selectionLabel: selectionLabel,
                            emptyMessage: 'No timeline is available for this historical trace.',
                            errorMessage: 'Trace timeline is unavailable for this request.'
                        });
                    }
                });
                chatSession.addEventListener('change', async function() {
                    activeSessionId = chatSession.value;
                    setSessionLabel(activeSessionId);
                    await loadSessionTranscript(activeSessionId);
                });
                chatNewSession.addEventListener('click', async function() {
                    chatNewSession.disabled = true;
                    try {
                        const created = await createSession();
                        await refreshSessions(created.sessionId);
                    } catch (e) {
                        chatMessages.innerHTML += '<div class="text-red-600 dark:text-red-400"><strong>Error</strong>: ' + esc(e.message || 'Failed to create session') + '</div>';
                    }
                    chatNewSession.disabled = false;
                });
                refreshSessions(null).catch(function(e) {
                    chatMessages.innerHTML = '<div class="text-red-600 dark:text-red-400">Error loading sessions: ' + esc(e.message || 'Unknown error') + '</div>';
                });
                chatSend.addEventListener('click', async function() {
                    const prompt = chatInput.value.trim();
                    if (!prompt) return;
                    if (!activeSessionId) {
                        try {
                            const created = await createSession();
                            activeSessionId = created.sessionId;
                            await refreshSessions(activeSessionId);
                        } catch (e) {
                            chatMessages.innerHTML += '<div class="text-red-600 dark:text-red-400"><strong>Error</strong>: ' + esc(e.message || 'Failed to initialize session') + '</div>';
                            return;
                        }
                    }
                    const agentId = chatAgent.value;
                    const statusEl = document.getElementById('codegraph-chat-status');
                    const bannerEl = document.getElementById('codegraph-chat-completion-banner');
                    const isCodingAgent = agentId === 'coder-agent' || agentId === 'refactor-agent';

                    chatSend.disabled = true;
                    chatSend.innerHTML = '<span class="inline-flex items-center gap-2"><svg class="animate-spin h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path></svg>Processing...</span>';
                    if (statusEl) {
                        statusEl.className = 'mt-2 flex items-center gap-2 px-3 py-2 rounded-lg bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 text-blue-800 dark:text-blue-200 text-sm';
                        statusEl.innerHTML = '<svg class="animate-spin h-4 w-4 flex-shrink-0" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path></svg><span>' + (isCodingAgent ? 'Edit → Compile → Test pipeline running…' : 'Agent working…') + '</span>';
                        statusEl.classList.remove('hidden');
                    }
                    if (bannerEl) { bannerEl.classList.add('hidden'); bannerEl.innerHTML = ''; }
                    chatMessages.innerHTML += '<div class="text-gray-600 dark:text-gray-300"><strong>You</strong>: ' + esc(prompt) + '</div>';
                    chatInput.value = '';
                    if (window.agctorTraceTimeline) {
                        ensureTraceTimelineMounted();
                        window.agctorTraceTimeline.clear('codegraph-trace-timeline', 'Processing trace…', 'Latest prompt in progress');
                    }
                    try {
                        const res = await fetch('/api/agents/' + encodeURIComponent(agentId) + '/message', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ payload: prompt, sessionId: activeSessionId })
                        });
                        const data = await res.json().catch(function() { return {}; });
                        const traceId = data.traceId || data.TraceId || data.traceID || data.trace_id;
                        if (window.agctorTraceTimeline) {
                            ensureTraceTimelineMounted();
                            if (traceId) {
                                window.agctorTraceTimeline.load('codegraph-trace-timeline', traceId, {
                                    selectionLabel: 'Latest live trace',
                                    emptyMessage: 'No timeline is available for this request.',
                                    errorMessage: 'Trace timeline is unavailable for this request.'
                                });
                            } else if (res.ok && isCodingAgent) {
                                window.agctorTraceTimeline.render('codegraph-trace-timeline', {
                                    events: [
                                        { sequence: 1, depth: 0, startOffsetMs: 0, durationMs: 1, label: 'Edit/Process', hasResult: true }
                                    ],
                                    totalDurationMs: 1
                                }, { selectionLabel: 'Latest live trace' });
                            }
                        }
                        function formatReply(rd) {
                            if (rd == null) return 'No response.';
                            if (typeof rd === 'string') {
                                try {
                                    var parsed = JSON.parse(rd);
                                    if (parsed && typeof parsed === 'object') {
                                        if (parsed.isSuccess === true && parsed.output) return 'Success: ' + parsed.output;
                                        if (parsed.isSuccess === false && parsed.error) return 'Error: ' + parsed.error;
                                    }
                                } catch (_) {}
                                return rd;
                            }
                            if (rd.isSuccess === true && rd.output) return 'Success: ' + rd.output;
                            if (rd.isSuccess === false && rd.error) return 'Error: ' + rd.error;
                            return JSON.stringify(rd);
                        }
                        const reply = res.ok && data.responseData != null ? formatReply(data.responseData) : (data.errorMessage || data.message || 'No response.');
                        const status = res.ok ? 'text-green-700 dark:text-green-300' : 'text-red-600 dark:text-red-400';
                        var isSuccess = false;
                        if (res.ok && data.responseData != null) {
                            try {
                                var rd = typeof data.responseData === 'string' ? JSON.parse(data.responseData) : data.responseData;
                                isSuccess = rd && rd.isSuccess === true;
                            } catch (_) {
                                var r = String(reply);
                                isSuccess = r.indexOf('Success:') === 0 || (r.indexOf('File ') === 0 && r.indexOf('updated') >= 0) || (r.indexOf('Error:') !== 0 && r.indexOf('Refactor failed') !== 0);
                            }
                        }
                        await loadSessionTranscript(activeSessionId, null, traceId || null);

                        if (statusEl) statusEl.classList.add('hidden');

                        if (res.ok && isCodingAgent && bannerEl) {
                            bannerEl.className = 'mt-3 px-4 py-3 rounded-lg border-2 flex items-center gap-3 ' + (isSuccess ? 'bg-green-50 dark:bg-green-900/20 border-green-500 dark:border-green-600' : 'bg-amber-50 dark:bg-amber-900/20 border-amber-500 dark:border-amber-600');
                            bannerEl.innerHTML = (isSuccess
                                ? '<span class="flex-shrink-0 w-10 h-10 rounded-full bg-green-500 flex items-center justify-center text-white text-xl">✓</span><div><p class="font-semibold text-green-800 dark:text-green-200">Coding complete</p><p class="text-sm text-green-700 dark:text-green-300 mt-0.5">' + esc(reply) + '</p></div>'
                                : '<span class="flex-shrink-0 w-10 h-10 rounded-full bg-amber-500 flex items-center justify-center text-white text-xl">!</span><div><p class="font-semibold text-amber-800 dark:text-amber-200">Coding finished with issues</p><p class="text-sm text-amber-700 dark:text-amber-300 mt-0.5">' + esc(reply) + '</p></div>');
                            bannerEl.classList.remove('hidden');
                            setTimeout(function() {
                                bannerEl.classList.add('opacity-0');
                                bannerEl.style.transition = 'opacity 0.4s ease';
                                setTimeout(function() { bannerEl.classList.add('hidden'); bannerEl.classList.remove('opacity-0'); }, 400);
                            }, 6000);
                        }
                        var updated = await fetch('/api/CodeGraph/current').then(function(r) { return r.ok ? r.json() : null; });
                        if (updated) {
                            var countEl = document.getElementById('codegraph-vector-count');
                            var stateEl = document.getElementById('codegraph-embedding-state');
                            var graphVersionEl = document.getElementById('codegraph-graph-version');
                            var indexedVersionEl = document.getElementById('codegraph-indexed-graph-version');
                            var lastIndexedEl = document.getElementById('codegraph-last-indexed-at');
                            var errorEl = document.getElementById('codegraph-embedding-error');
                            var updatedCount = updated.embeddingStoreSummary != null ? (updated.embeddingStoreSummary.vectorCount ?? 0) : 0;
                            if (updated.actorTree && window.agctorActorTree) {
                                window.agctorActorTree.render('codegraph-actor-tree', updated.actorTree);
                            }
                            if (countEl) countEl.textContent = String(updatedCount);
                            if (stateEl && updated.embeddingStoreSummary) stateEl.textContent = String(updated.embeddingStoreSummary.state ?? 'Unknown');
                            if (graphVersionEl && updated.embeddingStoreSummary) graphVersionEl.textContent = String(updated.embeddingStoreSummary.graphVersion ?? 0);
                            if (indexedVersionEl && updated.embeddingStoreSummary) indexedVersionEl.textContent = String(updated.embeddingStoreSummary.indexedGraphVersion ?? 0);
                            if (lastIndexedEl && updated.embeddingStoreSummary) lastIndexedEl.textContent = String(updated.embeddingStoreSummary.lastIndexedAt ?? 'Not yet indexed');
                            if (errorEl && updated.embeddingStoreSummary) errorEl.textContent = updated.embeddingStoreSummary.lastError ? ('Last error: ' + updated.embeddingStoreSummary.lastError) : '';
                            setLoadVectorsButtonState(document.getElementById('load-vectors-btn'), updatedCount);
                        }
                        // After coding (coder/refactor), trigger re-index so Actor tree shows new methods
                        if (res.ok && (agentId === 'coder-agent' || agentId === 'refactor-agent')) {
                            var traceHost = document.getElementById('codegraph-trace-host');
                            var idxStatus = document.createElement('div');
                            idxStatus.className = 'mb-2 px-3 py-2 rounded-lg bg-blue-50 dark:bg-blue-900/20 text-blue-700 dark:text-blue-300 text-sm';
                            idxStatus.textContent = 'Updating Actor tree…';
                            if (traceHost) traceHost.insertBefore(idxStatus, traceHost.firstChild);
                            fetch('/api/agents/embedding-coordinator-agent/message', {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ payload: 'index' })
                            }).then(function(idxRes) {
                                if (!idxRes || !idxRes.ok) { if (idxStatus) { idxStatus.textContent = 'Actor tree update skipped'; idxStatus.remove(); } return null; }
                                return fetch('/api/CodeGraph/current');
                              })
                              .then(function(r) { return r && r.ok ? r.json() : null; })
                              .then(function(refreshed) {
                                if (idxStatus) {
                                    idxStatus.textContent = 'Actor tree updated';
                                    idxStatus.className = 'mb-2 px-3 py-2 rounded-lg bg-green-50 dark:bg-green-900/20 text-green-700 dark:text-green-300 text-sm';
                                    setTimeout(function() { idxStatus.remove(); }, 3000);
                                }
                                if (refreshed && refreshed.actorTree) {
                                    if (window.agctorActorTree) {
                                        window.agctorActorTree.render('codegraph-actor-tree', refreshed.actorTree);
                                    }
                                }
                                if (refreshed && refreshed.embeddingStoreSummary) {
                                    var c = document.getElementById('codegraph-vector-count');
                                    var s = document.getElementById('codegraph-embedding-state');
                                    var gv = document.getElementById('codegraph-graph-version');
                                    var iv = document.getElementById('codegraph-indexed-graph-version');
                                    var li = document.getElementById('codegraph-last-indexed-at');
                                    var err = document.getElementById('codegraph-embedding-error');
                                    if (c) c.textContent = String(refreshed.embeddingStoreSummary.vectorCount ?? 0);
                                    if (s) s.textContent = String(refreshed.embeddingStoreSummary.state ?? 'Unknown');
                                    if (gv) gv.textContent = String(refreshed.embeddingStoreSummary.graphVersion ?? 0);
                                    if (iv) iv.textContent = String(refreshed.embeddingStoreSummary.indexedGraphVersion ?? 0);
                                    if (li) li.textContent = String(refreshed.embeddingStoreSummary.lastIndexedAt ?? 'Not yet indexed');
                                    if (err) err.textContent = refreshed.embeddingStoreSummary.lastError ? ('Last error: ' + refreshed.embeddingStoreSummary.lastError) : '';
                                    setLoadVectorsButtonState(document.getElementById('load-vectors-btn'), refreshed.embeddingStoreSummary.vectorCount ?? 0);
                                }
                              })
                              .catch(function() { if (idxStatus) idxStatus.remove(); });
                        }
                        // Trace timeline is rendered early so later refresh errors do not hide visualization.
                    } catch (e) {
                        chatMessages.innerHTML += '<div class="text-red-600 dark:text-red-400"><strong>Error</strong>: ' + esc(e.message || 'Request failed') + '</div>';
                        chatMessages.scrollTop = chatMessages.scrollHeight;
                        if (statusEl) statusEl.classList.add('hidden');
                    }
                    chatSend.disabled = false;
                    chatSend.innerHTML = 'Send';
                });
            }
        })
        .catch(() => {
            if (panels) panels.classList.add('hidden');
            el.classList.remove('hidden');
            el.innerHTML = '<div class="p-6 bg-red-50 border border-red-200 rounded-lg dark:bg-gray-800 dark:border-red-800"><p class="text-red-700 dark:text-red-400">Failed to load CodeGraph context.</p></div>';
        });
})();
