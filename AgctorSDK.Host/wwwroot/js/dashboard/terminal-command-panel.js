/**
 * Reusable terminal command panel: preset picker, editable command, Run + live SSE output.
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
        var streamUrl = '/api/terminal/run/stream';

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
                var live = { stdout: '', stderr: '' };

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
                            : 'Streaming docker output… (' + secs + 's elapsed)';
                    }
                }, 1000);

                try {
                    var res = await fetch(streamUrl, {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'Accept': 'text/event-stream'
                        },
                        body: JSON.stringify({
                            command: command,
                            contextKey: root.getAttribute('data-context-key') || null,
                            contextType: root.getAttribute('data-context-type') || 'actor-runtime'
                        })
                    });

                    if (!res.ok) {
                        var errPayload = await res.json().catch(function () { return {}; });
                        var errMsg = errPayload.message || errPayload.Message || res.statusText;
                        var elapsedFail = Math.max(1, Math.floor((Date.now() - started) / 1000));
                        setStatus(statusEl, 'Failed after ' + elapsedFail + 's');
                        finishOutput(outputWrap, progressEl, exitEl, stdoutEl, stderrEl, '', errMsg, null, false);
                        return;
                    }

                    var finalEvt = await consumeSseStream(res, function (evt) {
                        if (evt.type === 'stdout' && evt.text) {
                            live.stdout += evt.text;
                            renderLive(stdoutEl, stderrEl, command, live);
                        } else if (evt.type === 'stderr' && evt.text) {
                            live.stderr += evt.text;
                            renderLive(stdoutEl, stderrEl, command, live);
                        }
                    });

                    var elapsed = Math.max(1, Math.floor((Date.now() - started) / 1000));
                    var ok = finalEvt && finalEvt.success === true;
                    var exitCode = finalEvt && finalEvt.exitCode != null ? finalEvt.exitCode : (ok ? 0 : 1);
                    var msg = (finalEvt && (finalEvt.message || finalEvt.Message)) || (ok ? 'Done.' : 'Failed.');

                    if (finalEvt && finalEvt.type === 'error') {
                        ok = false;
                        if (finalEvt.message) live.stderr += (live.stderr ? '\n' : '') + finalEvt.message;
                    }

                    setStatus(statusEl, ok ? ('Done in ' + elapsed + 's') : ('Failed in ' + elapsed + 's'));
                    finishOutput(
                        outputWrap,
                        progressEl,
                        exitEl,
                        stdoutEl,
                        stderrEl,
                        live.stdout,
                        live.stderr,
                        exitCode,
                        ok
                    );
                    if (!ok && msg) {
                        setStatus(statusEl, msg);
                    }

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

    /** Read SSE `data: {json}` frames from a fetch Response body. */
    async function consumeSseStream(res, onEvent) {
        if (!res.body || !res.body.getReader) {
            throw new Error('Streaming response body is not available in this browser.');
        }

        var reader = res.body.getReader();
        var decoder = new TextDecoder();
        var buffer = '';
        var lastDone = null;

        while (true) {
            var chunk = await reader.read();
            if (chunk.done) break;
            buffer += decoder.decode(chunk.value, { stream: true });

            var parts = buffer.split('\n\n');
            buffer = parts.pop() || '';

            for (var i = 0; i < parts.length; i++) {
                var frame = parts[i];
                if (!frame || frame.charAt(0) === ':') continue;
                var lines = frame.split('\n');
                var dataLines = [];
                for (var j = 0; j < lines.length; j++) {
                    var line = lines[j];
                    if (line.indexOf('data:') === 0) {
                        dataLines.push(line.slice(5).replace(/^\s/, ''));
                    }
                }
                if (!dataLines.length) continue;

                var raw = dataLines.join('\n');
                var evt;
                try {
                    evt = JSON.parse(raw);
                } catch (e) {
                    continue;
                }

                if (evt.type === 'done' || evt.type === 'error') {
                    lastDone = evt;
                }
                onEvent(evt);
            }
        }

        return lastDone;
    }

    function renderLive(stdoutEl, stderrEl, command, live) {
        if (stdoutEl) {
            var out = live.stdout && live.stdout.trim()
                ? live.stdout
                : '(streaming… docker often prints progress on stderr)';
            stdoutEl.textContent = '$ ' + command + '\n\n' + out;
            stdoutEl.scrollTop = stdoutEl.scrollHeight;
        }
        if (stderrEl) {
            if (live.stderr && live.stderr.trim()) {
                stderrEl.textContent = live.stderr;
                stderrEl.classList.remove('hidden');
                stderrEl.scrollTop = stderrEl.scrollHeight;
            }
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

    /** First-time image pulls can take several minutes; progress streams live into the panel. */
    function getDockerWaitHint(command) {
        var cmd = (command || '').toLowerCase();
        if (cmd.indexOf('cognee-mcp') >= 0 && (cmd.indexOf(' pull') >= 0 || cmd.indexOf(' up ') >= 0)) {
            return 'Downloading/starting Cognee MCP (~6 GB on first pull; often 2–5 min)';
        }
        if (cmd.indexOf('graphiti') >= 0 && (cmd.indexOf(' pull') >= 0 || cmd.indexOf(' up ') >= 0)) {
            return 'Downloading/starting Graphiti + Neo4j (first pull can take 2–4 min)';
        }
        if (cmd.indexOf('lightrag') >= 0 && (cmd.indexOf(' pull') >= 0 || cmd.indexOf(' up ') >= 0)) {
            return 'Downloading/starting LightRAG (first pull can take 1–3 min)';
        }
        if (cmd.indexOf(' pull') >= 0) {
            return 'Pulling Docker image(s) — live progress below';
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
        stdoutEl.textContent = '$ ' + command + '\n\n(streaming…)';
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
