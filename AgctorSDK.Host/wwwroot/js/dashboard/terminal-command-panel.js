/**
 * Reusable terminal command panel: preset picker, editable command, Run + live progress output.
 */
(function () {
    function initPanel(root) {
        if (!root || root.dataset.terminalBound === '1') return;
        root.dataset.terminalBound = '1';

        var input = root.querySelector('.terminal-command-input');
        var preset = root.querySelector('select[id$="-preset"]');
        var runBtn = root.querySelector('.terminal-command-run');
        var runLabel = root.querySelector('.terminal-command-run-label');
        var spinner = root.querySelector('.terminal-command-spinner');
        var copyBtn = root.querySelector('.terminal-command-copy');
        var statusEl = root.querySelector('.terminal-command-status');
        var outputWrap = root.querySelector('.terminal-command-output');
        var progressEl = root.querySelector('.terminal-command-progress');
        var exitEl = root.querySelector('.terminal-command-exit');
        var stdoutEl = root.querySelector('.terminal-command-stdout');
        var stderrEl = root.querySelector('.terminal-command-stderr');
        var runUrl = '/api/terminal/run';

        if (preset && input) {
            preset.addEventListener('change', function () {
                if (preset.value) input.value = preset.value;
            });
        }

        if (copyBtn && input) {
            copyBtn.addEventListener('click', function () {
                navigator.clipboard.writeText(input.value).then(function () {
                    setStatus(statusEl, 'Copied.');
                }).catch(function () {
                    input.select();
                    document.execCommand('copy');
                    setStatus(statusEl, 'Copied.');
                });
            });
        }

        if (runBtn && input) {
            runBtn.addEventListener('click', async function () {
                var command = input.value.trim();
                if (!command) {
                    setStatus(statusEl, 'Enter a command.');
                    return;
                }

                var tickTimer = null;
                var started = Date.now();

                runBtn.disabled = true;
                setRunning(true, runBtn, runLabel, spinner);
                setStatus(statusEl, 'Running… 0s');
                showProgress(outputWrap, progressEl, exitEl, stdoutEl, stderrEl, command);

                tickTimer = setInterval(function () {
                    var secs = Math.floor((Date.now() - started) / 1000);
                    setStatus(statusEl, 'Running… ' + secs + 's');
                    if (progressEl) {
                        var hint = getDockerWaitHint(command);
                        progressEl.textContent = hint
                            ? hint + ' (' + secs + 's elapsed)'
                            : 'Waiting for docker… (' + secs + 's elapsed)';
                    }
                }, 1000);

                try {
                    var res = await fetch(runUrl, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            command: command,
                            contextKey: root.getAttribute('data-context-key') || null,
                            contextType: root.getAttribute('data-context-type') || 'actor-runtime'
                        })
                    });

                    var payload = await res.json().catch(function () { return {}; });
                    var elapsed = Math.max(1, Math.floor((Date.now() - started) / 1000));

                    if (!res.ok) {
                        var errMsg = payload.message || payload.Message || res.statusText;
                        setStatus(statusEl, 'Failed after ' + elapsed + 's');
                        finishOutput(outputWrap, progressEl, exitEl, stdoutEl, stderrEl, '', errMsg, null, false);
                        return;
                    }

                    var ok = payload.success === true;
                    var exitCode = payload.exitCode != null ? payload.exitCode : (payload.ExitCode != null ? payload.ExitCode : (ok ? 0 : 1));
                    var msg = payload.message || payload.Message || (ok ? 'Done.' : 'Failed.');
                    setStatus(statusEl, ok ? ('Done in ' + elapsed + 's') : ('Failed in ' + elapsed + 's'));
                    finishOutput(
                        outputWrap,
                        progressEl,
                        exitEl,
                        stdoutEl,
                        stderrEl,
                        payload.stdOut || payload.stdout || '',
                        payload.stdErr || payload.stderr || '',
                        exitCode,
                        ok
                    );
                    if (!ok && msg) {
                        setStatus(statusEl, msg);
                    }

                    // Tell the dashboard to refresh Docker state after compose commands.
                    document.dispatchEvent(new CustomEvent('agctor-docker-changed', {
                        detail: {
                            contextKey: root.getAttribute('data-context-key') || null,
                            command: command,
                            success: ok
                        }
                    }));
                } catch (err) {
                    setStatus(statusEl, 'Network error.');
                    finishOutput(outputWrap, progressEl, exitEl, stdoutEl, stderrEl, '', String(err), -1, false);
                } finally {
                    if (tickTimer) clearInterval(tickTimer);
                    runBtn.disabled = false;
                    setRunning(false, runBtn, runLabel, spinner);
                }
            });
        }
    }

    function setRunning(isRunning, btn, label, spinner) {
        if (label) label.textContent = isRunning ? 'Running…' : 'Run';
        if (spinner) spinner.classList.toggle('hidden', !isRunning);
        if (btn) btn.classList.toggle('bg-blue-500', isRunning);
    }

    function setStatus(el, text) {
        if (el) el.textContent = text;
    }

    /** First-time image pulls can take several minutes; docker compose does not stream progress to the browser. */
    function getDockerWaitHint(command) {
        var cmd = (command || '').toLowerCase();
        if (cmd.indexOf('cognee-mcp') >= 0 && (cmd.indexOf(' pull') >= 0 || cmd.indexOf(' up ') >= 0)) {
            return 'Downloading/starting Cognee MCP (~6 GB on first pull; often 2–5 min)';
        }
        if (cmd.indexOf('lightrag') >= 0 && (cmd.indexOf(' pull') >= 0 || cmd.indexOf(' up ') >= 0)) {
            return 'Downloading/starting LightRAG (first pull can take 1–3 min)';
        }
        if (cmd.indexOf(' pull') >= 0) {
            return 'Pulling Docker image(s) — no live progress until complete';
        }
        if (cmd.indexOf(' up ') >= 0 || cmd.indexOf(' up -d') >= 0) {
            return 'Starting Docker service(s)';
        }
        return null;
    }

    function showProgress(wrap, progressEl, exitEl, stdoutEl, stderrEl, command) {
        if (!wrap || !stdoutEl) return;
        wrap.classList.remove('hidden');
        if (progressEl) {
            progressEl.textContent = 'Starting: ' + command;
            progressEl.classList.remove('hidden');
        }
        if (exitEl) exitEl.textContent = '';
        stdoutEl.textContent = '$ ' + command + '\n\n(executing…)';
        if (stderrEl) {
            stderrEl.textContent = '';
            stderrEl.classList.add('hidden');
        }
    }

    function finishOutput(wrap, progressEl, exitEl, stdoutEl, stderrEl, stdout, stderr, exitCode, ok) {
        if (!wrap || !stdoutEl) return;
        wrap.classList.remove('hidden');
        if (progressEl) progressEl.classList.add('hidden');
        if (exitEl) {
            exitEl.textContent = exitCode != null ? ('exit ' + exitCode + (ok ? ' ✓' : ' ✗')) : '';
        }
        stdoutEl.textContent = stdout && stdout.trim() ? stdout : '(no stdout)';
        if (stderrEl) {
            if (stderr && stderr.trim()) {
                stderrEl.textContent = stderr;
                stderrEl.classList.remove('hidden');
            } else {
                stderrEl.classList.add('hidden');
            }
        }
    }

    document.querySelectorAll('[data-terminal-command-panel]').forEach(initPanel);

    window.AgctorTerminalCommandPanel = { init: initPanel };
})();
