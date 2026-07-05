/**
 * Actor runtime dashboard: runtime selection, config save, Docker sidecar controls + live status.
 */
(function () {
    const root = document.getElementById('actor-runtime-dashboard');
    if (!root) return;

    const banner = document.getElementById('actor-runtime-banner');
    const bannerText = document.getElementById('actor-runtime-banner-text');
    const dockerPanel = document.getElementById('docker-sidecar-panel');
    const dockerActionStatus = document.getElementById('docker-action-status');

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

    function applyRuntimeHealth(data) {
        if (!data) return;

        var badge = document.getElementById('runtime-health-badge');
        if (badge) {
            badge.textContent = data.overallStatus || 'unknown';
            badge.className = overallHealthBadgeClass(data.overallStatus);
        }

        var detail = document.getElementById('runtime-health-detail');
        if (detail) {
            var text = data.detail || '';
            detail.textContent = text;
            detail.classList.toggle('hidden', !text);
        }
    }

    async function refreshRuntimeHealth() {
        var btn = document.getElementById('runtime-health-refresh');
        if (btn) btn.disabled = true;
        try {
            var res = await fetch('/api/runtime/health');
            if (!res.ok) return null;
            var data = await res.json();
            applyRuntimeHealth(data);
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

        var availableEl = document.getElementById('docker-status-available');
        if (availableEl) {
            availableEl.textContent = data.dockerAvailable ? 'Available' : 'Not available';
        }

        var stateEl = document.getElementById('docker-status-state');
        if (stateEl) {
            stateEl.textContent = data.state || 'unknown';
            stateEl.className = stateBadgeClass(data.state);
        }

        var serviceEl = document.getElementById('docker-status-service');
        if (serviceEl) serviceEl.textContent = data.serviceName || '—';

        var healthEl = document.getElementById('docker-status-health');
        if (healthEl) healthEl.textContent = data.health || '—';

        var statusTextEl = document.getElementById('docker-status-text');
        if (statusTextEl) {
            var row = statusTextEl.closest('.sm\\:col-span-2') || statusTextEl.parentElement;
            if (data.statusText) {
                statusTextEl.textContent = data.statusText;
                if (row) row.classList.remove('hidden');
            } else if (row) {
                row.classList.add('hidden');
            }
        }

        var messageEl = document.getElementById('docker-status-message');
        if (messageEl) messageEl.textContent = data.message || '';
    }

    async function refreshDockerStatus() {
        if (!dockerPanel) return null;
        var runtimeId = dockerPanel.getAttribute('data-docker-runtime-id');
        if (!runtimeId) return null;

        try {
            var res = await fetch('/api/runtime/docker/' + encodeURIComponent(runtimeId));
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
        var runtimeId = dockerPanel.getAttribute('data-docker-runtime-id');
        if (!runtimeId) return;

        if (action === 'refresh') {
            setDockerActionMessage('Refreshing…');
            await refreshDockerStatus();
            await refreshRuntimeHealth();
            setDockerActionMessage('');
            return;
        }

        setDockerButtonsDisabled(true);
        setDockerActionMessage(action.charAt(0).toUpperCase() + action.slice(1) + ' in progress…');

        try {
            var res = await fetch('/api/runtime/docker/' + encodeURIComponent(runtimeId) + '/' + action, {
                method: 'POST'
            });
            var payload = await res.json().catch(function () { return {}; });
            var ok = res.ok && payload.success !== false;
            var msg = payload.message || payload.Message || (ok ? 'Done.' : 'Action failed.');
            setDockerActionMessage(msg, !ok);
            await refreshDockerStatus();
            await refreshRuntimeHealth();
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

        // Live status on load and every 10s so terminal/manual docker changes show up.
        refreshDockerStatus();
        refreshRuntimeHealth();
        setInterval(function () {
            refreshDockerStatus();
            refreshRuntimeHealth();
        }, 10000);

        document.addEventListener('agctor-docker-changed', function (ev) {
            var key = ev.detail && ev.detail.contextKey;
            var runtimeId = dockerPanel.getAttribute('data-docker-runtime-id');
            if (!key || !runtimeId || key.toLowerCase() === runtimeId.toLowerCase()) {
                refreshDockerStatus();
                refreshRuntimeHealth();
            }
        });
    }

    var healthRefreshBtn = document.getElementById('runtime-health-refresh');
    if (healthRefreshBtn) {
        healthRefreshBtn.addEventListener('click', refreshRuntimeHealth);
    } else if (document.getElementById('runtime-health-badge')) {
        // No Docker panel but health row is shown (e.g. InMemory).
        refreshRuntimeHealth();
        setInterval(refreshRuntimeHealth, 10000);
    }

    root.querySelectorAll('[data-runtime-select]').forEach(function (btn) {
        btn.addEventListener('click', async function () {
            var id = btn.getAttribute('data-runtime-select');
            if (!id) return;

            btn.disabled = true;
            try {
                var statusRes = await fetch('/api/runtime');
                if (!statusRes.ok) {
                    alert('Could not load current settings.');
                    return;
                }
                var status = await statusRes.json();
                var cfg = status.configured || {};
                var experimental = id === 'Orleans' || id === 'Proto.Actor';
                var body = {
                    defaultRuntime: id,
                    allowExperimentalRuntimes: experimental ? true : cfg.allowExperimentalRuntimes,
                    protoHost: cfg.protoHost,
                    protoPort: cfg.protoPort,
                    orleansClusterId: cfg.orleansClusterId,
                    orleansServiceId: cfg.orleansServiceId,
                    orleansGatewayHost: cfg.orleansGatewayHost,
                    orleansGatewayPort: cfg.orleansGatewayPort
                };

                var res = await fetch('/api/runtime', {
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
                window.location.href = '/Dashboard/ActorRuntime';
            } catch (_) {
                alert('Request failed.');
            } finally {
                btn.disabled = false;
            }
        });
    });

    var form = document.getElementById('runtime-save-form');
    if (form) {
        form.addEventListener('submit', async function (ev) {
            ev.preventDefault();
            var fd = new FormData(form);
            var body = {
                defaultRuntime: String(fd.get('defaultRuntime') || ''),
                allowExperimentalRuntimes: fd.get('allowExperimentalRuntimes') === 'true'
            };
            var ph = fd.get('protoHost');
            if (ph != null && String(ph).trim()) body.protoHost = String(ph).trim();
            var pp = fd.get('protoPort');
            if (pp != null && String(pp).trim()) body.protoPort = parseInt(String(pp), 10);
            var oc = fd.get('orleansClusterId');
            if (oc != null && String(oc).trim()) body.orleansClusterId = String(oc).trim();
            var os = fd.get('orleansServiceId');
            if (os != null && String(os).trim()) body.orleansServiceId = String(os).trim();
            var gh = fd.get('orleansGatewayHost');
            if (gh != null && String(gh).trim()) body.orleansGatewayHost = String(gh).trim();
            var gp = fd.get('orleansGatewayPort');
            if (gp != null && String(gp).trim()) body.orleansGatewayPort = parseInt(String(gp), 10);

            var res = await fetch('/api/runtime', {
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
            window.location.href = '/Dashboard/ActorRuntime';
        });
    }

    window.AgctorActorRuntimeDashboard = {
        refreshDockerStatus: refreshDockerStatus,
        refreshRuntimeHealth: refreshRuntimeHealth
    };
})();
