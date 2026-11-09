(function () {
    'use strict';
    const $ = id => document.getElementById('vr-' + id);
    const params = new URLSearchParams(location.search);
    const scenario = params.get('scenario') || 'scene-video-chain';
    let runId = params.get('run'), current, cursor = 0, questionId, polling = false, requestId, requestBody;
    let starting = false, loadingStories = false;
    function updateStoryMode() {
        const continuing = $('mode-continue').checked;
        $('existing-story').hidden = !continuing;
        $('workspace').disabled = !continuing || loadingStories;
        $('workspace').required = continuing;
        $('start-button').disabled = starting || (continuing && (loadingStories || !$('workspace').value));
        $('story-help').textContent = continuing
            ? 'Only the selected story’s relevant characters and continuity will be used. New shots will be saved to that story.'
            : 'We’ll create fresh Scene Memory automatically. Other stories and library designs won’t be added.';
    }
    async function loadStories() {
        if (loadingStories) return;
        loadingStories = true; updateStoryMode(); $('refresh-stories').disabled = true;
        const selected = $('workspace').value;
        try {
            const stories = await api('/api/agent-runs/stories');
            const placeholder = text('option', stories.length ? 'Choose a story…' : 'No stories yet — choose New story'); placeholder.value = '';
            $('workspace').replaceChildren(placeholder);
            stories.forEach(story => { const option = text('option', story.name); option.value = story.workspaceId; $('workspace').append(option); });
            if (stories.some(story => story.workspaceId === selected)) $('workspace').value = selected;
        } catch (err) { error(err); }
        finally { loadingStories = false; $('refresh-stories').disabled = false; updateStoryMode(); }
    }
    $('mode-new').onchange = updateStoryMode;
    $('mode-continue').onchange = () => { updateStoryMode(); loadStories(); };
    $('workspace').onchange = updateStoryMode;
    $('refresh-stories').onclick = loadStories;
    async function api(url, body) {
        const response = await fetch(url, body === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(data.error || data.detail || 'The request could not be completed. Please try again.');
        return data;
    }
    function error(e) { $('error').textContent = e.message; }
    function text(tag, value) { const node = document.createElement(tag); node.textContent = value; return node; }
    function render(run) {
        current = run;
        Array.from($('recent').querySelectorAll('a')).filter(a => a.dataset.runId === run.id).forEach(a => { a.textContent = new Date(run.createdAt).toLocaleString() + ' · ' + run.status; });
        $('status').textContent = run.status;
        $('summary').textContent = run.summary;
        $('cancel').hidden = run.isTerminal;
        const state = run.state;
        const input = state.Input || state.input || {};
        const storyName = state.StoryName || state.storyName;
        const storyMode = input.StoryMode || input.storyMode || 'continue';
        $('story-info').hidden = !storyName;
        $('story-info').textContent = storyName ? `${storyMode === 'new' ? 'New story' : 'Continuing story'}: ${storyName}` : '';
        // The checkpoint is serialized by the runtime, independently of the HTTP serializer.
        const clips = state.Clips || state.clips || [];
        $('clips').replaceChildren(...clips.map((clip, i) => {
            const c = Object.fromEntries(Object.entries(clip).map(([k, v]) => [k[0].toLowerCase() + k.slice(1), v]));
            const row = text('li', `Clip ${i + 1} · ${c.durationSeconds}s · ${c.status}`);
            if (c.progress || c.status === 'completed' || c.status === 'rendering') {
                const p = Object.fromEntries(Object.entries(c.progress || {}).map(([k, v]) => [k[0].toLowerCase() + k.slice(1), v]));
                const completed = c.status === 'completed';
                const percent = completed ? 100 : Number.isFinite(p.percent) && p.percent >= 0 ? Math.min(99, p.percent) : null;
                const stale = c.lastCheckSucceeded === false || c.status === 'failed';
                const box = document.createElement('div'); box.className = 'vr-progress' + (stale ? ' vr-progress-stale' : '');
                const label = text('strong', completed ? '100% · Video ready' : percent === null
                    ? 'Progress percentage not reported yet'
                    : `${stale ? 'Last reported estimate' : 'Estimated clip progress'} · ${percent}%`);
                box.append(label);
                if (completed || !stale) {
                    const bar = document.createElement('progress'); bar.max = 100;
                    if (percent !== null) bar.value = percent;
                    bar.setAttribute('aria-label', `Clip ${i + 1} ${completed ? 'completion' : 'estimated progress'}`);
                    box.append(bar);
                }
                if (p.stage && !completed) box.append(text('p', p.stage));
                if (p.detail && !completed) box.append(text('p', p.detail));
                if (stale) box.append(text('p', 'Not a live percentage. Waiting for a successful status update.'));
                row.append(box);
            }
            if (c.lastCheckedAt) {
                const checked = new Date(c.lastCheckedAt).toLocaleTimeString();
                const detail = c.lastCheckSucceeded === false
                    ? `Status check unsuccessful at ${checked}. Last known renderer status: ${c.rendererStatus || 'unknown'}.`
                    : `Renderer: ${c.rendererStatus || c.status} · Last checked ${checked}.`;
                const freshness = text('p', detail); freshness.className = 'vr-muted'; row.append(freshness);
            }
            if (c.submittedAt && c.status === 'rendering') {
                const minutes = Math.max(0, Math.floor((Date.now() - Date.parse(c.submittedAt)) / 60000));
                row.append(text('p', `Submitted ${minutes} min ago. This is the same render job.`));
            }
            row.append(text('p', c.prompt));
            if (c.videoUrl && /^https?:\/\//i.test(c.videoUrl)) {
                const link = text('a', 'Open video ↗'); link.href = c.videoUrl; link.target = '_blank'; link.rel = 'noopener'; row.append(link);
            }
            return row;
        }));
        const q = run.status === 'waiting' ? run.question : null;
        const watching = q && run.phase === 'poll' && ['timeout', 'retry'].includes(q.kind);
        $('monitor').hidden = !watching;
        $('monitor').textContent = watching
            ? 'Automatic status checks are active, even when this page is closed. Next check around ' + new Date(run.dueAt).toLocaleTimeString() + '.' : '';
        $('question').hidden = !q;
        if (q) $('question-text').textContent = q.prompt;
        if (q && q.id !== questionId) {
            questionId = q.id; $('question-text').textContent = q.prompt;
            $('choices').replaceChildren(text('legend', 'Choose an answer'));
            q.choices.forEach(choice => {
                const label = document.createElement('label'), radio = document.createElement('input');
                radio.type = 'radio'; radio.name = 'answer'; radio.value = choice.id;
                label.append(radio, document.createTextNode(choice.label)); $('choices').append(label);
            });
            $('answer').value = ''; $('answer').disabled = !q.allowFreeText;
        }
        if (!q) questionId = null;
    }
    async function poll() {
        if (!runId || polling) return;
        polling = true;
        const id = runId;
        try {
            const run = await api('/api/agent-runs/' + encodeURIComponent(id));
            const events = await api('/api/agent-runs/' + encodeURIComponent(id) + '/events?after=' + cursor);
            if (runId !== id) return;
            render(run); $('error').textContent = '';
            events.forEach(event => {
                const row = text('li', event.agent + ' · ' + event.message);
                row.prepend(text('time', new Date(event.at).toLocaleTimeString())); $('events').prepend(row); cursor = event.sequence;
            });
            while ($('events').children.length > 200) $('events').lastChild.remove();
        } catch (e) { error(new Error('Live updates disconnected. Retrying automatically. ' + e.message)); }
        finally { polling = false; }
    }
    function openRun(id) {
        runId = id; cursor = 0; questionId = null; $('events').replaceChildren();
        history.replaceState(null, '', '?scenario=' + encodeURIComponent(scenario) + '&run=' + encodeURIComponent(id));
        poll();
    }
    async function recent() {
        const runs = await api('/api/agent-runs?scenarioId=' + encodeURIComponent(scenario));
        $('recent').replaceChildren(...runs.slice(0, 12).map(run => {
            const link = text('a', new Date(run.createdAt).toLocaleString() + ' · ' + run.status);
            link.dataset.runId = run.id;
            link.href = '?scenario=' + encodeURIComponent(scenario) + '&run=' + run.id;
            link.onclick = e => { e.preventDefault(); openRun(run.id); }; return link;
        }));
    }
    $('start').onsubmit = async e => {
        e.preventDefault(); starting = true; updateStoryMode(); $('error').textContent = '';
        const storyMode = $('mode-continue').checked ? 'continue' : 'new';
        const body = { storyMode, workspaceId: storyMode === 'continue' ? $('workspace').value : null, prompt: $('prompt').value, totalSeconds: +$('total').value,
            clipSeconds: +$('clip').value, model: $('model').value || null, reviewPlan: $('review').checked };
        // Reuse the same admission key after an ambiguous network failure, but not after changing inputs.
        const fingerprint = JSON.stringify(body);
        if (!requestId || fingerprint !== requestBody) { requestId = crypto.randomUUID(); requestBody = fingerprint; }
        try { const run = await api('/api/scenarios/' + encodeURIComponent(scenario) + '/runs', { ...body, requestId }); openRun(run.id); requestId = null; await recent(); }
        catch (err) { error(err); } finally { starting = false; updateStoryMode(); }
    };
    $('question').onsubmit = async e => {
        e.preventDefault(); const button = e.submitter; button.disabled = true;
        try { render(await api('/api/agent-runs/' + runId + '/reply', { revision: current.revision, questionId,
            choice: $('choices').querySelector('input:checked')?.value || null, text: $('answer').value || null })); }
        catch (err) { error(err); } finally { button.disabled = false; }
    };
    $('cancel').onclick = async () => { try { render(await api('/api/agent-runs/' + runId + '/cancel', {})); await recent(); } catch (err) { error(err); } };
    async function init() {
        try {
            const connection = await api('/api/agent-runs/connection');
            $('connection').textContent = connection.rendererConfigured ? 'Cynelo rendering endpoint configured.' : 'Cynelo rendering is not connected yet. The workflow will pause before rendering.';
            updateStoryMode();
            await recent(); await poll();
        } catch (err) { error(err); }
    }
    init(); setInterval(poll, 2000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) poll(); });
    window.addEventListener('focus', poll);
})();
