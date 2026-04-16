/**
 * Maintenance: show status, persist project root, validate, rebuild.
 */
(function () {
    const statusEl = document.getElementById('pm-root-status');
    const input = /** @type {HTMLInputElement} */ (document.getElementById('pm-root-input'));
    const msg = document.getElementById('pm-root-msg');
    const form = document.getElementById('pm-root-form');
    const out = document.getElementById('pm-maintain-output');
    if (!statusEl || !input || !form || !out) return;

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    function renderIssues(issues) {
        if (!Array.isArray(issues) || issues.length === 0) return '<p class="text-green-700 dark:text-green-300">No issues.</p>';
        let h = '<ul class="list-disc pl-5 space-y-1">';
        for (let i = 0; i < issues.length; i++) {
            const x = issues[i];
            const err = x.isError ? 'text-red-700 dark:text-red-300' : 'text-amber-800 dark:text-amber-200';
            h +=
                '<li class="' +
                err +
                '"><code>' +
                esc(x.code || '') +
                '</code> — ' +
                esc(x.message || '') +
                (x.path ? ' <span class="font-mono text-xs">' + esc(x.path) + '</span>' : '') +
                '</li>';
        }
        h += '</ul>';
        return h;
    }

    function refreshStatus() {
        fetch('/api/project-memory/status')
            .then(function (r) {
                return r.ok ? r.json() : null;
            })
            .then(function (st) {
                if (!st) {
                    statusEl.textContent = '(status unavailable)';
                    return;
                }
                if (st.projectRoot) input.value = st.projectRoot;
                var active = esc(st.projectRoot || '(not set)');
                var sample = esc(st.defaultSampleProjectRoot || '');
                var badge =
                    st.usesDefaultSampleProjectRoot === true
                        ? '<span class="inline-block mt-2 px-2 py-0.5 text-xs font-medium rounded bg-blue-100 text-blue-900 dark:bg-blue-900/50 dark:text-blue-100">Using built-in sample default</span>'
                        : '';
                statusEl.innerHTML =
                    '<div class="space-y-2">' +
                    '<div><span class="text-gray-500 dark:text-gray-400">Active root:</span><br /><span class="font-mono text-sm">' +
                    active +
                    '</span></div>' +
                    (sample
                        ? '<div class="text-xs text-gray-500 dark:text-gray-400">Built-in sample default (when config is empty):<br /><span class="font-mono">' +
                          sample +
                          '</span></div>'
                        : '') +
                    badge +
                    '</div>';
            });
    }

    refreshStatus();

    form.addEventListener('submit', function (ev) {
        ev.preventDefault();
        const path = input.value.trim();
        if (!path) {
            msg.textContent = 'Enter a path.';
            return;
        }
        msg.textContent = 'Saving…';
        fetch('/api/project-memory/project-root', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ projectRoot: path })
        })
            .then(function (r) {
                if (!r.ok) return r.json().then(function (b) {
                    throw new Error(b.error || JSON.stringify(b));
                });
                return r.json();
            })
            .then(function (j) {
                msg.textContent = j.note || 'Saved.';
                refreshStatus();
            })
            .catch(function (e) {
                msg.textContent = esc(e.message || String(e));
            });
    });

    document.getElementById('pm-btn-validate').addEventListener('click', function () {
        out.innerHTML = '<p>Validating…</p>';
        fetch('/api/project-memory/validate', { method: 'POST' })
            .then(function (r) {
                return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
            })
            .then(function (res) {
                out.innerHTML =
                    '<p class="font-medium ' +
                    (res.success ? 'text-green-700 dark:text-green-300' : 'text-amber-800 dark:text-amber-200') +
                    '">' +
                    (res.success ? 'Validation passed (no errors).' : 'Validation completed with issues.') +
                    '</p>' +
                    renderIssues(res.issues);
            })
            .catch(function (e) {
                out.innerHTML = '<p class="text-red-600">' + esc(e.message || '') + '</p>';
            });
    });

    document.getElementById('pm-btn-rebuild').addEventListener('click', function () {
        if (!window.confirm('Run full project rebuild?')) return;
        out.innerHTML = '<p>Rebuilding…</p>';
        fetch('/api/project-memory/rebuild', { method: 'POST' })
            .then(function (r) {
                return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
            })
            .then(function (res) {
                const logLine = res.logPath
                    ? '<p class="text-xs font-mono break-all mt-2">Log: ' + esc(res.logPath) + '</p>'
                    : '';
                out.innerHTML =
                    '<p class="font-medium ' +
                    (res.success ? 'text-green-700 dark:text-green-300' : 'text-red-700 dark:text-red-300') +
                    '">' +
                    (res.success ? 'Rebuild finished successfully.' : 'Rebuild finished with errors.') +
                    '</p>' +
                    logLine +
                    renderIssues(res.issues);
            })
            .catch(function (e) {
                out.innerHTML = '<p class="text-red-600">' + esc(e.message || '') + '</p>';
            });
    });
})();
