/**
 * RAG providers dashboard: provider selection, config save, Docker sidecar, test query.
 */
(function () {
    const root = document.getElementById('rag-providers-dashboard');
    if (!root) return;

    const banner = document.getElementById('rag-providers-banner');
    const bannerText = document.getElementById('rag-providers-banner-text');
    const dockerPanel = document.getElementById('rag-docker-panel');
    const dockerActionStatus = document.getElementById('rag-docker-action-status');

    function showBanner(message, isError) {
        if (!banner || !bannerText) return;
        bannerText.textContent = message;
        banner.classList.remove('hidden');
        banner.classList.toggle('rp-error', !!isError);
    }

    function overallHealthBadgeClass(status) {
        if ((status || '').toLowerCase() === 'healthy') {
            return 'text-xs font-medium px-2 py-0.5 rounded bg-green-100 text-green-800 dark:bg-green-900/40 dark:text-green-200';
        }
        return 'text-xs font-medium px-2 py-0.5 rounded bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-200';
    }

    function applyRagHealth(data) {
        if (!data) return;

        var badge = document.getElementById('rag-health-badge');
        if (badge) {
            badge.textContent = (data.overallStatus || '').toLowerCase() === 'healthy' ? 'Healthy' : (data.overallStatus || '').toLowerCase() === 'degraded' ? 'Needs attention' : 'Status unknown';
            badge.className = overallHealthBadgeClass(data.overallStatus);
        }

        var name = document.getElementById('rag-current-name');
        if (name && data.providerId) name.textContent = data.providerId === 'None' ? 'Markdown only (no RAG)' : data.providerId;
        var checked = document.getElementById('rag-health-checked');
        if (checked) checked.textContent = 'Checked ' + new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        var detail = document.getElementById('rag-health-detail');
        if (detail) {
            var text = data.detail || '';
            detail.textContent = text;
            detail.classList.toggle('hidden', !text);
        }
    }

    async function refreshRagHealth() {
        var btn = document.getElementById('rag-health-refresh');
        if (btn) btn.disabled = true;
        try {
            var res = await fetch('/api/rag-providers/health');
            if (!res.ok) throw new Error('Health check failed');
            var data = await res.json();
            applyRagHealth(data);
            return data;
        } catch (_) {
            var checked = document.getElementById('rag-health-checked');
            if (checked) checked.textContent = 'Could not refresh. Showing the last known status.';
            return null;
        } finally {
            if (btn) btn.disabled = false;
        }
    }

    function stateBadgeClass(state) {
        var s = (state || 'unknown').toLowerCase();
        if (s === 'running') {
            return 'text-xs font-medium px-2 py-0.5 rounded bg-green-100 text-green-800 dark:bg-green-900/40 dark:text-green-200';
        }
        if (s === 'stopped' || s === 'exited') {
            return 'text-xs font-medium px-2 py-0.5 rounded bg-gray-100 text-gray-800 dark:bg-gray-700 dark:text-gray-200';
        }
        if (s === 'error' || s === 'docker_unavailable' || s === 'missing_compose') {
            return 'text-xs font-medium px-2 py-0.5 rounded bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-200';
        }
        return 'text-xs font-medium px-2 py-0.5 rounded bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-200';
    }

    function setDockerActionMessage(text, isError) {
        if (!dockerActionStatus) return;
        if (!text) {
            dockerActionStatus.classList.add('hidden');
            dockerActionStatus.textContent = '';
            return;
        }
        dockerActionStatus.textContent = text;
        dockerActionStatus.classList.remove('hidden');
        dockerActionStatus.classList.toggle('text-red-600', !!isError);
        dockerActionStatus.classList.toggle('dark:text-red-400', !!isError);
        dockerActionStatus.classList.toggle('text-gray-600', !isError);
        dockerActionStatus.classList.toggle('dark:text-gray-300', !isError);
    }

    function applyDockerStatus(data) {
        if (!data) return;

        var availableEl = document.getElementById('rag-docker-status-available');
        if (availableEl) {
            availableEl.textContent = data.dockerAvailable ? 'Available' : 'Not available';
        }

        var summaryState = document.getElementById('rag-docker-summary-state');
        if (summaryState) { summaryState.textContent = data.state || 'unknown'; summaryState.className = stateBadgeClass(data.state); }
        var stateEl = document.getElementById('rag-docker-status-state');
        if (stateEl) {
            stateEl.textContent = data.state || 'unknown';
            stateEl.className = stateBadgeClass(data.state);
        }

        var serviceEl = document.getElementById('rag-docker-status-service');
        if (serviceEl) serviceEl.textContent = data.serviceName || '—';

        var healthEl = document.getElementById('rag-docker-status-health');
        if (healthEl) healthEl.textContent = data.health || '—';

        var messageEl = document.getElementById('rag-docker-status-message');
        if (messageEl) messageEl.textContent = data.message || '';
    }

    async function refreshDockerStatus() {
        if (!dockerPanel) return null;
        var providerId = dockerPanel.getAttribute('data-docker-provider-id');
        if (!providerId) return null;

        try {
            var res = await fetch('/api/rag-providers/docker/' + encodeURIComponent(providerId));
            if (!res.ok) throw new Error('Service status unavailable');
            var data = await res.json();
            applyDockerStatus(data);
            return data;
        } catch (_) {
            var summary = document.getElementById('rag-docker-summary-state');
            if (summary) summary.textContent = 'Refresh failed';
            setDockerActionMessage('Could not refresh service status. Showing the last known reading.', true);
            return null;
        }
    }

    function setDockerButtonsDisabled(disabled) {
        if (!dockerPanel) return;
        dockerPanel.querySelectorAll('[data-docker-action]').forEach(function (btn) {
            btn.disabled = disabled;
        });
    }

    async function runDockerAction(action) {
        if (!dockerPanel) return;
        var providerId = dockerPanel.getAttribute('data-docker-provider-id');
        if (!providerId) return;

        if (action === 'refresh') {
            setDockerActionMessage('Refreshing…');
            var refreshed = await refreshDockerStatus();
            await refreshRagHealth();
            if (refreshed) setDockerActionMessage('Service status updated.');
            return;
        }

        setDockerButtonsDisabled(true);
        setDockerActionMessage(action.charAt(0).toUpperCase() + action.slice(1) + ' in progress…');

        try {
            var res = await fetch('/api/rag-providers/docker/' + encodeURIComponent(providerId) + '/' + action, {
                method: 'POST'
            });
            var payload = await res.json().catch(function () { return {}; });
            var ok = res.ok && payload.success !== false;
            var msg = payload.message || payload.Message || (ok ? 'Done.' : 'Action failed.');
            setDockerActionMessage(msg, !ok);
            await refreshDockerStatus();
            await refreshRagHealth();
        } catch (err) {
            setDockerActionMessage(String(err), true);
        } finally {
            setDockerButtonsDisabled(false);
        }
    }

    if (dockerPanel) {
        dockerPanel.querySelectorAll('[data-docker-action]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var action = btn.getAttribute('data-docker-action');
                if (action) runDockerAction(action);
            });
        });

        refreshDockerStatus();
        refreshRagHealth();
        setInterval(function () {
            refreshDockerStatus();
            refreshRagHealth();
        }, 10000);

        document.addEventListener('agctor-docker-changed', function (ev) {
            var key = ev.detail && ev.detail.contextKey;
            var providerId = dockerPanel.getAttribute('data-docker-provider-id');
            if (!key || !providerId || key.toLowerCase() === providerId.toLowerCase()) {
                refreshDockerStatus();
                refreshRagHealth();
            }
        });
    }

    var healthRefreshBtn = document.getElementById('rag-health-refresh');
    if (healthRefreshBtn) {
        healthRefreshBtn.addEventListener('click', refreshRagHealth);
    } else if (document.getElementById('rag-health-badge')) {
        refreshRagHealth();
        setInterval(refreshRagHealth, 10000);
    }

    // Choosing a provider only opens its settings. Activation is an explicit form submission.
    var form = document.getElementById('rag-save-form');
    var initialForm = form ? JSON.stringify(Array.from(new FormData(form).entries())) : '';
    var saving = false;
    var working = false;
    var pendingNavigation = null;
    var returnFocus = null;
    var leaveDialog = document.getElementById('rag-leave-dialog');
    function isDirty() { return !!form && JSON.stringify(Array.from(new FormData(form).entries())) !== initialForm; }
    function updateDraftState() {
        var dirty = isDirty();
        var draft = document.getElementById('rag-draft-state');
        if (draft) draft.textContent = dirty ? 'Unsaved connection edits' : 'Saved settings';
        var reset = document.getElementById('rag-reset-settings');
        if (reset) { reset.classList.toggle('hidden', !dirty); reset.disabled = saving || working; }
        root.querySelectorAll('[data-draft-notice]').forEach(function (el) { el.classList.toggle('hidden', !dirty); });
        var save = document.getElementById('rag-save-btn');
        if (save) save.disabled = saving || working || (!dirty && root.dataset.selectedProvider === root.dataset.activeProvider);
        ['rag-test-run-btn', 'rag-ingest-run-btn', 'rag-ingest-preview-btn'].forEach(function (id) {
            var btn = document.getElementById(id); if (btn) btn.disabled = dirty || saving || working;
        });
    }
    function selectPanel(key, focus) {
        root.querySelectorAll('[role="tab"]').forEach(function (tab) {
            var active = tab.id === 'rag-tab-' + key;
            tab.setAttribute('aria-selected', String(active)); tab.tabIndex = active ? 0 : -1;
            document.getElementById(tab.getAttribute('aria-controls')).classList.toggle('hidden', !active);
            if (active && focus) tab.focus();
        });
    }
    var tabs = Array.from(root.querySelectorAll('[role="tab"]'));
    tabs.forEach(function (tab, index) {
        tab.addEventListener('click', function () { selectPanel(tab.id.replace('rag-tab-', '')); });
        tab.addEventListener('keydown', function (e) {
            if (['ArrowLeft','ArrowRight','Home','End'].indexOf(e.key) < 0) return;
            e.preventDefault();
            var next = e.key === 'Home' ? 0 : e.key === 'End' ? tabs.length - 1 : (index + (e.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
            selectPanel(tabs[next].id.replace('rag-tab-', ''), true);
        });
    });
    function closeLeaveDialog() {
        leaveDialog.classList.add('hidden'); pendingNavigation = null;
        if (returnFocus) returnFocus.focus();
    }
    document.getElementById('rag-keep-editing').onclick = closeLeaveDialog;
    document.getElementById('rag-discard-leave').onclick = function () {
        var href = pendingNavigation; initialForm = JSON.stringify(Array.from(new FormData(form).entries()));
        closeLeaveDialog(); if (href) window.location.href = href;
    };
    leaveDialog.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') closeLeaveDialog();
        if (e.key !== 'Tab') return;
        var first = document.getElementById('rag-keep-editing'), last = document.getElementById('rag-discard-leave');
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    });
    root.querySelectorAll('[data-provider-select]').forEach(function (link) {
        link.addEventListener('click', function (e) {
            if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
            if (saving || working) { e.preventDefault(); showBanner('Wait for the current operation to finish before changing providers.', true); return; }
            if (!isDirty()) return;
            e.preventDefault(); pendingNavigation = link.href; returnFocus = link;
            leaveDialog.classList.remove('hidden'); document.getElementById('rag-keep-editing').focus();
        });
    });
    window.addEventListener('beforeunload', function (e) {
        if (isDirty() || working) { e.preventDefault(); e.returnValue = ''; }
    });
    if (form) {
        form.addEventListener('input', updateDraftState);
        form.addEventListener('change', updateDraftState);
        var reset = document.getElementById('rag-reset-settings');
        if (reset) reset.onclick = function () { form.reset(); updateDraftState(); document.getElementById('rag-save-status').textContent = 'Connection edits discarded.'; };
        form.addEventListener('submit', async function (ev) {
            ev.preventDefault();
            if (saving || working) return;
            var fd = new FormData(form);
            var providerId = String(fd.get('defaultProvider') || '');
            var body = { defaultProvider: providerId };
            if (providerId === 'LightRAG') {
                body.lightRAG = { baseUrl: String(fd.get('baseUrl') || '').trim(), apiKey: String(fd.get('apiKey') || ''), defaultMode: String(fd.get('defaultMode') || 'Hybrid'), transport: String(fd.get('transport') || 'Rest') };
            } else if (providerId === 'Graphiti') {
                body.graphiti = { baseUrl: String(fd.get('baseUrl') || '').trim(), apiKey: String(fd.get('apiKey') || ''), defaultGroupId: String(fd.get('defaultGroupId') || 'agctor'), transport: String(fd.get('transport') || 'Rest') };
            } else if (providerId === 'Cognee') {
                body.cognee = { baseUrl: String(fd.get('baseUrl') || '').trim(), mcpPath: String(fd.get('mcpPath') || '/mcp'), searchType: String(fd.get('searchType') || 'CHUNKS'), llmApiKey: String(fd.get('llmApiKey') || ''), transport: 'McpHttp' };
            }
            var status = document.getElementById('rag-save-status');
            saving = true; updateDraftState();
            var fields = document.getElementById('rag-connection-fields');
            if (fields) fields.disabled = true;
            status.textContent = 'Saving settings and updating the default…'; status.classList.remove('rp-error');
            try {
                var res = await fetch('/api/rag-providers', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
                var payload = await res.json().catch(function () { return {}; });
                if (!res.ok) throw new Error(payload.message || payload.Message || 'Save failed (' + res.status + ').');
                if (fields) fields.disabled = false;
                initialForm = JSON.stringify(Array.from(new FormData(form).entries()));
                try { sessionStorage.setItem('rag-settings-saved', 'Settings saved. ' + (providerId === 'None' ? 'Markdown only' : providerId) + ' is now your default provider.'); } catch (_) { /* Storage may be disabled. */ }
                window.location.href = '/Dashboard/RagProviders?provider=' + encodeURIComponent(providerId);
            } catch (err) {
                status.textContent = err.message || 'Could not save settings. Please try again.'; status.classList.add('rp-error');
            } finally {
                if (fields) fields.disabled = false;
                saving = false; updateDraftState();
            }
        });
    }
    try { var saved = sessionStorage.getItem('rag-settings-saved'); if (saved) { showBanner(saved); sessionStorage.removeItem('rag-settings-saved'); } } catch (_) { /* Storage may be disabled. */ }
    updateDraftState();

    var testForm = document.getElementById('rag-test-query-form');
    if (testForm) {
        testForm.addEventListener('submit', async function (ev) {
            ev.preventDefault();
            var queryInput = document.getElementById('rag-test-query-input');
            var collectionInput = document.getElementById('rag-test-collection-id');
            var statusEl = document.getElementById('rag-test-query-status');
            var resultsEl = document.getElementById('rag-test-query-results');
            var runBtn = document.getElementById('rag-test-run-btn');
            var providerId = ingestPanel ? ingestPanel.getAttribute('data-provider-id') : null;
            var isCognee = (providerId || '').toLowerCase() === 'cognee';

            var query = queryInput ? String(queryInput.value || '').trim() : '';
            if (!query || isDirty() || saving || working) return;
            working = true; updateDraftState();

            if (runBtn) runBtn.disabled = true;
            var elapsedMinutes = 0;
            var queryTimer = setInterval(function () {
                elapsedMinutes += 1;
                if (statusEl) {
                    statusEl.textContent = isCognee
                        ? 'Running Cognee test query… ' + elapsedMinutes + ' min elapsed (first query may download tokenizer models).'
                        : 'Running test query… ' + elapsedMinutes + ' min elapsed.';
                }
            }, 60000);
            if (statusEl) {
                statusEl.textContent = isCognee
                    ? 'Running Cognee test query… chunk retrieval usually completes within 1–2 minutes.'
                    : 'Running test query…';
                statusEl.classList.remove('hidden');
            }
            if (resultsEl) resultsEl.innerHTML = '';

            try {
                var body = { query: query, topK: 8, providerId: providerId };
                if (collectionInput && String(collectionInput.value || '').trim()) {
                    body.collectionId = String(collectionInput.value).trim();
                }

                var controller = new AbortController();
                var timeoutMs = isCognee ? 300000 : 120000;
                var timeoutId = setTimeout(function () { controller.abort(); }, timeoutMs);

                var res = await fetch('/api/rag-providers/query', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body),
                    signal: controller.signal
                });
                clearTimeout(timeoutId);
                var payload = await res.json().catch(function () { return {}; });
                var ok = res.ok && payload.success === true;
                if (statusEl) {
                    statusEl.textContent = payload.message || payload.Message || (ok ? 'Done.' : 'Query failed.');
                    statusEl.classList.toggle('text-red-600', !ok);
                    statusEl.classList.toggle('dark:text-red-400', !ok);
                }

                var chunks = payload.chunks || [];
                if (resultsEl && ok && !chunks.length) { var empty = document.createElement('li'); empty.className = 'rp-empty-result'; empty.textContent = 'No matching excerpts. Check that files are indexed and the collection name matches, then try a more specific question.'; resultsEl.appendChild(empty); }
                if (resultsEl && ok && chunks.length > 0) {
                    chunks.forEach(function (chunk, idx) {
                        var li = document.createElement('li');
                        li.className = 'p-3 rounded border border-gray-200 dark:border-gray-700 bg-gray-50 dark:bg-gray-900/40';
                        var score = chunk.score != null ? ' · score ' + chunk.score : '';
                        var source = chunk.sourcePath ? ' · ' + chunk.sourcePath : '';
                        var preview = (chunk.text || '').slice(0, 400);
                        li.innerHTML = '<span class="font-medium text-gray-700 dark:text-gray-300">#' + (idx + 1) + score + source + '</span>' +
                            '<p class="mt-1 text-gray-600 dark:text-gray-300 whitespace-pre-wrap">' + escapeHtml(preview) + '</p>';
                        resultsEl.appendChild(li);
                    });
                }
            } catch (err) {
                if (statusEl) {
                    var msg = err && err.name === 'AbortError'
                        ? 'Query timed out. For Cognee, try CHUNKS search type or restart the cognee-mcp sidecar if it was stuck on a prior query.'
                        : String(err);
                    statusEl.textContent = msg;
                    statusEl.classList.add('text-red-600', 'dark:text-red-400');
                }
            } finally {
                clearInterval(queryTimer);
                clearTimeout(timeoutId);
                working = false; updateDraftState();
            }
        });
    }

    var ingestPanel = document.getElementById('rag-ingest-panel');
    var ingestForm = document.getElementById('rag-ingest-form');
    var ingestPreviewBtn = document.getElementById('rag-ingest-preview-btn');
    var ingestSourceSelect = document.getElementById('rag-ingest-source');
    var ingestStatusEl = document.getElementById('rag-ingest-status');
    var ingestPreviewList = document.getElementById('rag-ingest-preview-list');
    var ingestResultsEl = document.getElementById('rag-ingest-results');
    var ingestRunBtn = document.getElementById('rag-ingest-run-btn');
    var ingestBatchCount = 0;

    function getIngestProviderId() {
        return ingestPanel ? ingestPanel.getAttribute('data-provider-id') : null;
    }

    function isCogneeIngest() {
        return (getIngestProviderId() || '').toLowerCase() === 'cognee';
    }

    function buildIngestBody() {
        var sourceEl = document.getElementById('rag-ingest-source');
        var collectionEl = document.getElementById('rag-ingest-collection-id');
        var body = {
            sourceId: sourceEl ? String(sourceEl.value || 'agctor_markdown') : 'agctor_markdown',
            providerId: getIngestProviderId()
        };
        if (collectionEl && String(collectionEl.value || '').trim()) {
            body.collectionId = String(collectionEl.value).trim();
        }
        var forceEl = document.getElementById('rag-ingest-force-reingest');
        if (forceEl && forceEl.checked) {
            body.forceReingest = true;
        }
        return body;
    }

    function cogneeIngestStatusPrefix(elapsedMinutes) {
        var batches = ingestBatchCount > 0 ? ingestBatchCount : '?';
        var eta = ingestBatchCount > 0 ? ' (~' + (ingestBatchCount * 2) + ' min total for new datasets)' : '';
        return 'Ingesting into Cognee… ' + elapsedMinutes + ' min elapsed. '
            + batches + ' dataset batch(es); LLM graph extraction runs per batch' + eta + '.';
    }

    function setIngestStatus(text, isError) {
        if (!ingestStatusEl) return;
        ingestStatusEl.textContent = text;
        ingestStatusEl.classList.remove('hidden', 'text-red-600', 'dark:text-red-400', 'text-gray-600', 'dark:text-gray-300');
        ingestStatusEl.classList.add(isError ? 'text-red-600' : 'text-gray-600');
        if (isError) ingestStatusEl.classList.add('dark:text-red-400');
        else ingestStatusEl.classList.add('dark:text-gray-300');
    }

    async function loadIngestSources() {
        try {
            var res = await fetch('/api/rag-providers/ingest/sources');
            if (!res.ok) return;
            var payload = await res.json();
            var hint = document.getElementById('rag-ingest-project-root-hint');
            if (hint && payload.projectRoot) {
                hint.innerHTML = 'Project root: <code class="text-[11px]">' + escapeHtml(payload.projectRoot) + '</code>';
            } else if (hint && payload.projectRootConfigured === false) {
                hint.innerHTML = 'Set <code class="text-[11px]">Agctor:ProjectMemory:ProjectRoot</code> on the Maintenance page before ingesting.';
            }
        } catch (_) { /* optional */ }
    }

    if (ingestPreviewBtn) {
        ingestPreviewBtn.addEventListener('click', async function () {
            if (ingestSourceSelect && ingestSourceSelect.options[ingestSourceSelect.selectedIndex].disabled) {
                setIngestStatus('This data source is not implemented yet.', true);
                return;
            }
            if (isDirty() || saving || working) return;
            working = true; updateDraftState();
            setIngestStatus('Scanning source…', false);
            if (ingestPreviewList) {
                ingestPreviewList.innerHTML = '';
                ingestPreviewList.classList.add('hidden');
            }
            try {
                var res = await fetch('/api/rag-providers/ingest/preview', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(buildIngestBody())
                });
                var payload = await res.json().catch(function () { return {}; });
                ingestBatchCount = payload.datasetBatchCount || 0;
                setIngestStatus(payload.message || payload.Message || (res.ok ? 'Preview complete.' : 'Preview failed (' + res.status + ').'), !res.ok || payload.success !== true);
                var samples = payload.samplePaths || [];
                if (ingestPreviewList && samples.length > 0) {
                    ingestPreviewList.classList.remove('hidden');
                    samples.forEach(function (p) {
                        var li = document.createElement('li');
                        li.className = 'font-mono text-[11px]';
                        li.textContent = p;
                        ingestPreviewList.appendChild(li);
                    });
                }
            } catch (err) {
                setIngestStatus(String(err), true);
            } finally {
                working = false; updateDraftState();
            }
        });
    }

    if (ingestForm) {
        ingestForm.addEventListener('submit', async function (ev) {
            ev.preventDefault();
            if (ingestSourceSelect && ingestSourceSelect.options[ingestSourceSelect.selectedIndex].disabled) {
                setIngestStatus('This data source is not implemented yet.', true);
                return;
            }
            if (isDirty() || saving || working) return;
            working = true; updateDraftState();
            var elapsedMinutes = 0;
            var ingestTimer = setInterval(function () {
                elapsedMinutes += 1;
                var cogneeHint = isCogneeIngest()
                    ? cogneeIngestStatusPrefix(elapsedMinutes)
                    : '';
                setIngestStatus(
                    isCogneeIngest()
                        ? cogneeIngestStatusPrefix(elapsedMinutes)
                        : 'Ingesting… ' + elapsedMinutes + ' min elapsed.',
                    false);
            }, 60000);
            setIngestStatus(
                isCogneeIngest()
                    ? cogneeIngestStatusPrefix(0)
                    : 'Ingesting… this may take a while for large projects.',
                false);
            if (ingestResultsEl) ingestResultsEl.innerHTML = '';

            try {
                var res = await fetch('/api/rag-providers/ingest', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(buildIngestBody())
                });
                var payload = await res.json().catch(function () { return {}; });
                var ok = res.ok && payload.success === true;
                setIngestStatus(payload.message || payload.Message || (ok ? 'Done.' : 'Ingest failed.'), !ok);

                var items = payload.items || [];
                if (ingestResultsEl && items.length > 0) {
                    items.slice(0, 40).forEach(function (item) {
                        var li = document.createElement('li');
                        li.className = 'font-mono text-[11px] ' + (item.success ? 'text-green-700 dark:text-green-300' : 'text-red-700 dark:text-red-300');
                        li.textContent = (item.success ? '✓ ' : '✗ ') + item.relativePath + ' — ' + (item.message || '');
                        ingestResultsEl.appendChild(li);
                    });
                    if (items.length > 40) {
                        var more = document.createElement('li');
                        more.className = 'text-gray-500 dark:text-gray-400';
                        more.textContent = '… and ' + (items.length - 40) + ' more';
                        ingestResultsEl.appendChild(more);
                    }
                }
            } catch (err) {
                setIngestStatus(String(err), true);
            } finally {
                clearInterval(ingestTimer);
                working = false; updateDraftState();
            }
        });
    }

    if (ingestPanel) loadIngestSources();

    function escapeHtml(text) {
        var div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    window.AgctorRagProvidersDashboard = {
        refreshDockerStatus: refreshDockerStatus,
        refreshRagHealth: refreshRagHealth
    };
})();
