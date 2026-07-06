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

    function showBanner(message) {
        if (!banner || !bannerText) return;
        bannerText.textContent = message;
        banner.classList.remove('hidden');
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
            badge.textContent = data.overallStatus || 'unknown';
            badge.className = overallHealthBadgeClass(data.overallStatus);
        }

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
            if (!res.ok) return null;
            var data = await res.json();
            applyRagHealth(data);
            return data;
        } catch (_) {
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
            if (!res.ok) return null;
            var data = await res.json();
            applyDockerStatus(data);
            return data;
        } catch (_) {
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
            await refreshDockerStatus();
            await refreshRagHealth();
            setDockerActionMessage('');
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

    root.querySelectorAll('[data-provider-select]').forEach(function (btn) {
        btn.addEventListener('click', async function () {
            var id = btn.getAttribute('data-provider-select');
            if (!id) return;

            btn.disabled = true;
            try {
                var statusRes = await fetch('/api/rag-providers');
                if (!statusRes.ok) {
                    alert('Could not load current settings.');
                    return;
                }
                var status = await statusRes.json();
                var cfg = status.configured || {};
                var body = {
                    defaultProvider: id,
                    lightRAG: cfg.lightRAG,
                    cognee: cfg.cognee
                };

                var res = await fetch('/api/rag-providers', {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                });
                var payload = await res.json().catch(function () { return {}; });
                if (!res.ok) {
                    alert('Save failed: ' + (payload.message || payload.Message || res.statusText));
                    return;
                }

                showBanner(payload.message || payload.Message || 'Saved.');
                window.location.href = '/Dashboard/RagProviders?provider=' + encodeURIComponent(id);
            } catch (_) {
                alert('Request failed.');
            } finally {
                btn.disabled = false;
            }
        });
    });

    var form = document.getElementById('rag-save-form');
    if (form) {
        form.addEventListener('submit', async function (ev) {
            ev.preventDefault();
            var fd = new FormData(form);
            var providerId = String(fd.get('defaultProvider') || '');
            var body = { defaultProvider: providerId };

            if (providerId === 'LightRAG') {
                body.lightRAG = {
                    baseUrl: String(fd.get('baseUrl') || '').trim(),
                    apiKey: String(fd.get('apiKey') || ''),
                    defaultMode: String(fd.get('defaultMode') || 'Hybrid'),
                    transport: String(fd.get('transport') || 'Rest')
                };
            } else if (providerId === 'Cognee') {
                body.cognee = {
                    baseUrl: String(fd.get('baseUrl') || '').trim(),
                    mcpPath: String(fd.get('mcpPath') || '/mcp'),
                    searchType: String(fd.get('searchType') || 'RAG_COMPLETION'),
                    llmApiKey: String(fd.get('llmApiKey') || ''),
                    transport: String(fd.get('transport') || 'McpHttp')
                };
            }

            var res = await fetch('/api/rag-providers', {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
            var payload = await res.json().catch(function () { return {}; });
            if (!res.ok) {
                alert('Save failed: ' + (payload.message || payload.Message || res.statusText));
                return;
            }
            showBanner(payload.message || payload.Message || 'Saved.');
            window.location.href = '/Dashboard/RagProviders?provider=' + encodeURIComponent(providerId);
        });
    }

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
            if (!query) return;

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
                var ok = payload.success !== false;
                if (statusEl) {
                    statusEl.textContent = payload.message || payload.Message || (ok ? 'Done.' : 'Query failed.');
                    statusEl.classList.toggle('text-red-600', !ok);
                    statusEl.classList.toggle('dark:text-red-400', !ok);
                }

                var chunks = payload.chunks || [];
                if (resultsEl && chunks.length > 0) {
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
                if (runBtn) runBtn.disabled = false;
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
            ingestPreviewBtn.disabled = true;
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
                setIngestStatus(payload.message || payload.Message || 'Preview complete.', payload.success === false);
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
                ingestPreviewBtn.disabled = false;
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
            if (ingestRunBtn) ingestRunBtn.disabled = true;
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
                var ok = payload.success === true;
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
                if (ingestRunBtn) ingestRunBtn.disabled = false;
            }
        });
    }

    loadIngestSources();

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
