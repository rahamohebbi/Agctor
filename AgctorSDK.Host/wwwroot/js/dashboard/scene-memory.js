(() => {
    'use strict';
    const el = id => document.getElementById(id);
    let workspace = null;
    let busy = false;
    let agents = [];
    let promptReview = null;
    let originalExtraction = null;
    let draftExtraction = null;
    let editorDirty = false;
    let guidedOpen = false;
    let editorReturnLabel = null;
    let restoreEditorFocus = false;
    let promptAbortController = null;
    let promptProgressTimer = null;
    let promptStartedAt = 0;
    const url = () => `/api/scene-memory/workspaces/${encodeURIComponent(workspace.id)}`;

    async function api(path, body, signal) {
        const response = await fetch(path, body === undefined ? {} : {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal
        });
        const data = await response.json();
        if (!response.ok) {
            const error = new Error(data.error || data.message || Object.values(data.errors || {}).flat().join(' ') || data.title || 'Request failed.');
            error.diagnostics = data.diagnostics;
            throw error;
        }
        return data;
    }

    function availability() {
        el('sm-review-controls').disabled = busy;
        el('sm-guided-fields').disabled = busy;
        el('sm-close-editor').disabled = busy;
        el('sm-workspace').disabled = busy;
        el('sm-create-workspace').querySelector('button').disabled = busy;
        el('sm-entity-fields').disabled = busy || !workspace;
        el('sm-fact-fields').disabled = busy || !workspace?.entities.length;
        el('sm-holder-fields').disabled = busy || !workspace?.entities.some(e => e.kind === 'Object') || !workspace?.entities.some(e => e.kind === 'Character');
        el('sm-direct-fields').disabled = busy || !workspace;
        el('sm-create-agent-fields').disabled = busy || !workspace;
        el('sm-agent-fields').disabled = busy || !agents.length;
        el('sm-prompt-fields').disabled = busy || !workspace;
        el('sm-apply-prompt').disabled = busy || guidedOpen || editorDirty || !promptReview || promptReview.preview.extraction.questions.length > 0 ||
            (promptReview.proposed.history.length === workspace.history.length && promptReview.proposed.entities.length === workspace.entities.length);
        el('sm-discard-prompt').disabled = busy;
        el('sm-save-corrections').disabled = busy || !promptReview || !editorDirty;
        el('sm-reset-corrections').disabled = busy || !promptReview || !originalExtraction;
        ['sm-add-preview-entity', 'sm-add-preview-change', 'sm-add-preview-event'].forEach(id => { el(id).disabled = busy || !promptReview; });
        if (!busy && restoreEditorFocus) {
            restoreEditorFocus = false;
            const card = [...document.querySelectorAll('#sm-change-cards article, #sm-event-cards article')]
                .find(item => item.getAttribute('aria-label') === editorReturnLabel);
            (card?.querySelector('button') || el('sm-review-heading')).focus({ preventScroll: true });
        }
    }

    async function run(action) {
        if (busy) return;
        busy = true;
        el('sm-message').textContent = '';
        availability();
        try { await action(); }
        catch (error) { el('sm-message').textContent = error.message; }
        finally { busy = false; availability(); }
    }

    function options(id, items, blank) {
        const select = el(id), previous = select.value;
        select.replaceChildren();
        if (blank) select.add(new Option(blank, ''));
        items.forEach(item => select.add(new Option(item.name, item.id)));
        if ([...select.options].some(option => option.value === previous)) select.value = previous;
    }

    function table(target, headings, rows, empty) {
        const container = el(target);
        container.replaceChildren();
        if (!rows.length) { container.textContent = empty; return; }
        const table = document.createElement('table');
        const header = table.createTHead().insertRow();
        headings.forEach(text => {
            const th = document.createElement('th'); th.scope = 'col'; th.textContent = text; header.append(th);
        });
        const body = table.createTBody();
        rows.forEach(values => {
            const row = body.insertRow(); values.forEach(value => { row.insertCell().textContent = value; });
        });
        container.append(table);
    }

    function render() {
        clearPreview();
        el('sm-open-timeline').href = workspace ? `/Dashboard/SceneMemoryTimeline?workspaceId=${encodeURIComponent(workspace.id)}` : '/Dashboard/SceneMemoryTimeline';
        const entities = workspace?.entities || [], history = workspace?.history || [];
        options('sm-fact-entity', entities);
        options('sm-query-entity', entities, 'Use the question below');
        options('sm-holder-object', entities.filter(e => e.kind === 'Object'), 'Choose an object');
        options('sm-holder-character', entities.filter(e => e.kind === 'Character'), 'Choose a character');
        const possessions = entities.filter(e => e.kind === 'Object').map(item => {
            const fact = history.filter(f => f.entityId === item.id && f.property === 'held_by').at(-1);
            const states = new Map();
            history.filter(f => !f.isEvent && f.entityId === item.id && ['status', 'condition', 'disposition'].includes(f.property))
                .forEach(f => states.set(f.property, f));
            const status = [...states.values()].filter(f => !f.removed).map(f => f.value).join(' · ');
            const position = history.filter(f => f.entityId === item.id && f.property === 'carried_at').at(-1);
            const owner = history.filter(f => !f.isEvent && f.entityId === item.id && f.property === 'owned_by').at(-1);
            return [item.name, status || '—', owner && !owner.removed ? owner.value : 'Owner not recorded', fact ? (fact.removed ? 'Not held' : fact.value) : 'Holder not recorded',
                position && !position.removed ? position.value : '—', fact ? `#${fact.sequence}` : '—'];
        });
        table('sm-possession', ['Object', 'Status', 'Owned by', 'Held / carried by', 'Carrying position', 'Holder source'], possessions, 'Describe a scene to discover its objects, or add one manually.');
        holderPreview();
        const rows = [];
        entities.forEach(entity => {
            const facts = new Map();
            history.filter(f => !f.isEvent && f.entityId === entity.id).forEach(f => facts.set(f.property, f));
            if (![...facts.values()].some(f => !f.removed)) rows.push([entity.name, entity.kind, '—', 'No current facts', '—']);
            facts.forEach(f => { if (!f.removed) rows.push([entity.name, entity.kind, f.property, f.value, `#${f.sequence}`]); });
        });
        table('sm-current', ['Entity', 'Type', 'Property', 'Current value', 'Source change'], rows, 'Add an entity to begin.');
        table('sm-history', ['Change', 'Entity', 'Property', 'Value', 'Event note'], [...history].reverse().map(f =>
            [`#${f.sequence}`, entities.find(e => e.id === f.entityId)?.name || f.entityId, f.property, f.removed ? '(cleared)' : f.value, f.note || '—']), 'No changes recorded.');
        table('sm-events', ['Entry', 'What happened', 'Participants', 'Type', 'Source text'], history.filter(f => f.isEvent).reverse().map(f =>
            [`#${f.sequence}`, f.value, (f.participantIds || [f.entityId]).map(id => entities.find(e => e.id === id)?.name || id).join(', '), f.eventMode, f.note]), 'No events recorded yet. Describe an action and review its interpretation.');
        el('sm-history-title').textContent = `Change history (${history.length})`;
        const prompts = workspace?.prompts || [];
        el('sm-prompts-title').textContent = `Applied prompts (${prompts.length})`;
        table('sm-prompt-history', ['Scene', 'Shot', 'Prompt', 'Model', 'Changes'], [...prompts].reverse().map(p =>
            [p.sceneLabel || 'Unassigned', p.label || 'Untitled', p.prompt, p.model, p.lastSequence >= p.firstSequence ? `#${p.firstSequence}–#${p.lastSequence}` : 'Entities only']), 'No prompts applied yet.');
        document.querySelectorAll('.scene-memory input[name="atSequence"]').forEach(input => { input.max = history.length; });
        availability();
    }

    function holderPreview() {
        const item = workspace?.entities.find(e => e.id === el('sm-holder-object').value);
        const holder = workspace?.entities.find(e => e.id === el('sm-holder-character').value);
        const current = workspace?.history.filter(f => f.entityId === item?.id && f.property === 'held_by').at(-1);
        el('sm-holder-preview').textContent = !item ? 'Select an object to see its current holder.' :
            `${item.name}: ${current ? (current.removed ? 'not held' : current.value) : 'holder not recorded'}${holder ? ` → ${holder.name}` : ''}`;
    }

    async function loadWorkspace(id) {
        clearPreview();
        el('sm-prompt-status').textContent = '';
        workspace = null; agents = [];
        options('sm-agent', []);
        el('sm-direct-result').textContent = 'Results will appear here.';
        el('sm-agent-result').textContent = 'Agent → Scene Memory tool → saved facts';
        document.querySelectorAll('.scene-memory input[name="atSequence"]').forEach(input => { input.value = ''; });
        render();
        if (!id) return;
        workspace = await api(`/api/scene-memory/workspaces/${encodeURIComponent(id)}`);
        render();
        agents = await api(`${url()}/agents`);
        options('sm-agent', agents);
    }

    async function listWorkspaces(selected) {
        const list = await api('/api/scene-memory/workspaces');
        options('sm-workspace', list, list.length ? null : 'Create your first workspace');
        if (selected) el('sm-workspace').value = selected;
        await loadWorkspace(el('sm-workspace').value);
    }

    function form(id, action) {
        el(id).addEventListener('submit', event => {
            event.preventDefault();
            const data = Object.fromEntries(new FormData(event.currentTarget));
            run(() => action(data));
        });
    }

    function query(data) {
        return { entityId: data.entityId || null, question: data.question || null,
            atSequence: data.atSequence ? Number(data.atSequence) : null };
    }

    function result(id, data, prefix = '') {
        const sources = data.facts.map(f => `#${f.sequence} · ${f.property}: ${f.value}${f.note ? ` (${f.note})` : ''}`);
        el(id).textContent = `${prefix}${data.answer}\nAs of change #${data.atSequence}` +
            (sources.length ? `\n\nSource changes\n${sources.join('\n')}` : '');
    }

    function clearPreview() {
        promptReview = null;
        originalExtraction = null;
        draftExtraction = null;
        editorDirty = false;
        closeGuided();
        restoreEditorFocus = false;
        el('sm-prompt-preview').hidden = true;
        if (el('sm-prompt-diagnostics')) el('sm-prompt-diagnostics').hidden = true;
    }

    function showPromptDiagnostics(diagnostics, submittedPrompt) {
        if (!diagnostics?.issues?.length || !el('sm-prompt-diagnostics')) return;
        const prompt = diagnostics.prompt ?? submittedPrompt;
        const container = el('sm-diagnostics-issues');
        container.replaceChildren();
        el('sm-diagnostics-summary').textContent = `${diagnostics.model ? `Model: ${diagnostics.model}. ` : ''}` +
            (diagnostics.issues.some(i => i.code?.includes('timeout'))
                ? 'The model ran out of processing time. The highlighted section shows where processing stopped; it does not mean your wording is wrong.'
                : `${diagnostics.attempts?.length || 1} interpretation attempt(s) failed validation. These are problems in the generated interpretation; your prompt may already be clear.`);
        diagnostics.issues.forEach(issue => {
            const card = document.createElement('article');
            const sectionMatch = /^parts\\[(\\d+)\\]/.exec(issue.path || '');
            const pathMatch = /(changes|events)\[(\d+)\]/.exec(issue.path || '');
            const section = issue.path?.startsWith('continuity') ? 'Final continuity check · ' :
                sectionMatch ? `Section ${Number(sectionMatch[1]) + 1} · ` : '';
            const label = section + (pathMatch ? `${pathMatch[1] === 'changes' ? 'Change' : 'Story moment'} ${Number(pathMatch[2]) + 1}` : 'Scene interpretation');
            const subjectName = workspace?.entities.find(e => e.id === issue.subject)?.name || issue.subject;
            card.append(node('h4', `${label}${subjectName ? ` · ${subjectName}` : ''}`), node('p', issue.message));
            if (issue.property) card.append(node('p', `${issue.property.replaceAll('_', ' ')}: ${issue.value || issue.target || '(missing value)'}`));
            if (issue.evidence) {
                card.append(node('small', 'Supporting words supplied by the model'), node('blockquote', issue.evidence));
            }
            const matched = typeof prompt === 'string' && Number.isInteger(issue.sourceStart) && Number.isInteger(issue.sourceLength) &&
                issue.sourceStart >= 0 && issue.sourceLength > 0 &&
                prompt.slice(issue.sourceStart, issue.sourceStart + issue.sourceLength) === issue.evidence;
            if (matched) {
                const start = Math.max(0, issue.sourceStart - 100), end = Math.min(prompt.length, issue.sourceStart + issue.sourceLength + 100);
                const excerpt = node('blockquote');
                excerpt.append(document.createTextNode((start ? '…' : '') + prompt.slice(start, issue.sourceStart)),
                    node('mark', issue.evidence), document.createTextNode(prompt.slice(issue.sourceStart + issue.sourceLength, end) + (end < prompt.length ? '…' : '')));
                card.append(node('small', 'Matched passage in your prompt'), excerpt);
                const jump = node('button', 'Find in prompt'); jump.type = 'button'; jump.className = 'sm-secondary';
                jump.addEventListener('click', () => {
                    const input = el('sm-prompt-text');
                    if (input.value !== prompt) { el('sm-prompt-status').textContent = 'The prompt has changed. Preview again to check the new version.'; return; }
                    input.focus(); input.setSelectionRange(issue.sourceStart, issue.sourceStart + issue.sourceLength);
                    input.scrollIntoView({ block: 'center', behavior: 'smooth' });
                });
                card.append(jump);
            } else {
                card.append(node('p', 'No exact source passage could be located. The model may have omitted or rewritten the quote; this does not establish a problem with a particular sentence in your prompt.'));
            }
            card.append(node('p', issue.suggestion));
            container.append(card);
        });
        if (typeof prompt === 'string') {
            const details = node('details'); details.append(node('summary', 'Original prompt for this attempt'), node('pre', prompt)); container.append(details);
        }
        el('sm-diagnostics-report').textContent = JSON.stringify(diagnostics, null, 2);
        el('sm-prompt-diagnostics').hidden = false;
        el('sm-diagnostics-heading').focus();
    }

    function formatElapsed(milliseconds) {
        const seconds = Math.floor(milliseconds / 1000);
        return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
    }

    function updatePromptProgress(progress) {
        el('sm-preview-progress-time').textContent = formatElapsed(Date.now() - promptStartedAt);
        if (!progress) return;
        el('sm-progress-sent').dataset.state = 'done';
        el('sm-progress-model').dataset.state = progress.stage === 'validating' ? 'done' : 'active';
        el('sm-progress-ready').dataset.state = progress.stage === 'validating' ? 'active' : '';
        el('sm-preview-progress-title').textContent = progress.stage === 'continuity' ? 'Checking story continuity' : progress.totalParts
            ? `Section ${progress.part} of ${progress.totalParts} · ${progress.completedParts} checked`
            : progress.stage === 'waiting' ? 'Waiting to start your preview' : 'Checking your scene preview';
        el('sm-preview-progress-detail').textContent = progress.message + ' Saved memory is unchanged.';
    }

    async function streamPromptPreview(data, signal) {
        const response = await fetch(`${url()}/prompts/preview-stream`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data), signal
        });
        if (!response.ok) {
            const detail = await response.json().catch(() => ({}));
            throw Object.assign(new Error(detail.error || detail.title || 'Could not start the preview.'), { diagnostics: detail.diagnostics });
        }
        if (!response.body) throw new Error('This browser could not open the preview progress stream.');
        const reader = response.body.getReader(), decoder = new TextDecoder();
        let pending = '', review;
        function receive(line) {
            if (!line.trim()) return;
            const event = JSON.parse(line);
            if (event.type === 'progress') updatePromptProgress(event.progress);
            else if (event.type === 'result') review = event.review;
            else if (event.type === 'error') throw Object.assign(new Error(event.detail?.error || 'Preview failed. No changes were saved.'),
                { diagnostics: event.detail?.diagnostics });
        }
        try {
            while (true) {
                const { done, value } = await reader.read();
                pending += decoder.decode(value, { stream: !done });
                let newline;
                while ((newline = pending.indexOf('\n')) >= 0) {
                    receive(pending.slice(0, newline)); pending = pending.slice(newline + 1);
                }
                if (done) break;
            }
            receive(pending);
            if (!review) throw new Error('The preview connection ended before completion. No changes were saved. Please retry.');
            return review;
        } finally { await reader.cancel().catch(() => {}); reader.releaseLock(); }
    }

    function startPromptProgress(controller) {
        promptAbortController = controller;
        promptStartedAt = Date.now();
        el('sm-preview-progress').hidden = false;
        el('sm-cancel-interpret').disabled = false;
        el('sm-preview-progress-title').textContent = 'Starting your scene preview';
        el('sm-preview-progress-detail').textContent = 'Connecting to the selected model. Saved memory is unchanged.';
        el('sm-progress-sent').dataset.state = 'active';
        el('sm-progress-model').dataset.state = '';
        el('sm-progress-ready').dataset.state = '';
        updatePromptProgress();
        promptProgressTimer = window.setInterval(updatePromptProgress, 1000);
    }

    function stopPromptProgress() {
        if (promptProgressTimer !== null) window.clearInterval(promptProgressTimer);
        promptProgressTimer = null;
        promptAbortController = null;
        el('sm-preview-progress').hidden = true;
    }

    async function models() {
        try {
            const response = await api('/api/llm/models');
            options('sm-prompt-model', response.models.map(m => ({ id: m.name, name: m.name })), 'Use default chat model');
            el('sm-model-status').textContent = 'Choose a chat model with structured output support; dedicated decision or embedding models cannot interpret prompts.';
        } catch (error) { el('sm-model-status').textContent = error.message; }
    }

    const clone = value => JSON.parse(JSON.stringify(value));
    const kinds = ['Character', 'Object', 'Location', 'Audio', 'Music', 'Other'];
    const eventModes = ['occurred', 'ongoing', 'intended', 'hypothetical', 'negated'];

    function controlLabel(text, control) {
        const label = document.createElement('label');
        label.append(document.createTextNode(text), control);
        return label;
    }

    function textInput(value, maxLength, placeholder = '') {
        const input = document.createElement('input');
        input.value = value || ''; input.maxLength = maxLength; input.placeholder = placeholder;
        input.addEventListener('input', markEditorDirty);
        return input;
    }

    function selectInput(items, value, multiple = false) {
        const select = document.createElement('select');
        select.multiple = multiple;
        items.forEach(item => select.add(new Option(item.name, item.id, false,
            multiple ? (value || []).includes(item.id) : item.id === value)));
        // Keep invalid references visible so an edit never silently changes a relation to text.
        if (!multiple && value && !items.some(item => item.id === value)) select.add(new Option(`Unavailable: ${value}`, value));
        if (!multiple) select.value = value || '';
        select.addEventListener('change', markEditorDirty);
        return select;
    }

    function editorButton(text, action, danger = false) {
        const button = document.createElement('button');
        button.type = 'button'; button.textContent = text; button.className = `sm-secondary${danger ? ' sm-danger' : ''}`;
        button.addEventListener('click', action);
        return button;
    }

    function editorChoices() {
        return [
            ...(workspace?.entities || []).map(entity => ({ id: entity.id, name: `${entity.name} · existing ${entity.kind}` })),
            ...(draftExtraction?.entities || []).filter(entity => !(workspace?.entities || []).some(old => old.id === entity.ref))
                .map(entity => ({ id: entity.ref, name: `${entity.name || '(unnamed)'} · new ${entity.kind}` }))
        ];
    }

    function markEditorDirty() {
        editorDirty = true;
        document.querySelectorAll('#sm-change-cards button, #sm-event-cards button').forEach(button => { button.disabled = true; });
        el('sm-editor-status').textContent = guidedOpen ? 'Finish editing with Done to check the updated scene.' : 'Changes need checking. The cards still show the last checked result. Select Check changes in Advanced.';
        availability();
    }

    function emptyEditor(container, message) {
        const text = document.createElement('p'); text.className = 'sm-hint'; text.textContent = message; container.append(text);
    }

    function renderEditor() {
        const choices = editorChoices();
        const entityContainer = el('sm-preview-entities'); entityContainer.replaceChildren();
        if (!draftExtraction.entities.length) emptyEditor(entityContainer, 'No new entity identities. Existing scene entities are still available in state changes and events.');
        draftExtraction.entities.forEach((entity, index) => {
            const existing = (workspace?.entities || []).some(old => old.id === entity.ref);
            const row = document.createElement('div'); row.className = 'sm-editor-row sm-edit-entity'; row.dataset.index = index;
            const fields = document.createElement('div'); fields.className = 'sm-editor-fields';
            const name = textInput(entity.name, 100, 'Stable entity name'); name.className = 'sm-edit-entity-name'; name.disabled = existing;
            const kind = selectInput(kinds.map(item => ({ id: item, name: item })), entity.kind); kind.className = 'sm-edit-entity-kind'; kind.disabled = existing;
            fields.append(controlLabel('Name', name), controlLabel('Type', kind));
            const note = document.createElement('p'); note.className = 'sm-hint';
            note.textContent = existing ? 'Existing identity · name and type are fixed.' : `New identity reference: ${entity.ref}`;
            const actions = document.createElement('div'); actions.className = 'sm-actions';
            actions.append(editorButton('Remove entity', () => mutateEditor('entities', index, 'remove'), true));
            row.append(fields, note, actions); entityContainer.append(row);
        });

        const changeContainer = el('sm-preview-changes'); changeContainer.replaceChildren();
        if (!draftExtraction.changes.length) emptyEditor(changeContainer, 'No state changes proposed. Add one when the prompt establishes a lasting fact.');
        draftExtraction.changes.forEach((change, index) => {
            const row = document.createElement('div'); row.className = 'sm-editor-row sm-edit-change'; row.dataset.index = index;
            const fields = document.createElement('div'); fields.className = 'sm-editor-fields';
            const subject = selectInput(choices, change.subject); subject.className = 'sm-edit-change-subject';
            const property = textInput(change.property, 80, 'health, held_by, located_at…'); property.className = 'sm-edit-change-property';
            const value = textInput(change.value, 1000, 'Text value for an attribute'); value.className = 'sm-edit-change-value';
            const target = selectInput([{ id: '', name: 'No target · use text value' }, ...choices], change.target); target.className = 'sm-edit-change-target';
            const evidence = textInput(change.evidence, 2000, 'Exact text copied from the scene prompt'); evidence.className = 'sm-edit-change-evidence';
            const remove = document.createElement('input'); remove.type = 'checkbox'; remove.checked = change.remove; remove.className = 'sm-edit-change-remove';
            remove.addEventListener('change', markEditorDirty);
            fields.append(controlLabel('Entity', subject), controlLabel('Property', property), controlLabel('Text value', value),
                controlLabel('Relationship target', target), controlLabel('Exact source text', evidence), controlLabel('Clear this property', remove));
            const actions = document.createElement('div'); actions.className = 'sm-actions';
            actions.append(editorButton('Move up', () => mutateEditor('changes', index, 'up')),
                editorButton('Move down', () => mutateEditor('changes', index, 'down')),
                editorButton('Remove', () => mutateEditor('changes', index, 'remove'), true));
            row.append(fields, actions); changeContainer.append(row);
        });

        const eventContainer = el('sm-preview-events'); eventContainer.replaceChildren();
        if (!draftExtraction.events.length) emptyEditor(eventContainer, 'No events proposed. Add one to preserve an action independently from current state.');
        draftExtraction.events.forEach((event, index) => {
            const row = document.createElement('div'); row.className = 'sm-editor-row sm-edit-event'; row.dataset.index = index;
            const fields = document.createElement('div'); fields.className = 'sm-editor-fields';
            const description = textInput(event.description, 1000, 'What happened'); description.className = 'sm-edit-event-description';
            const mode = selectInput(eventModes.map(item => ({ id: item, name: item })), event.mode); mode.className = 'sm-edit-event-mode';
            const participants = selectInput(choices, event.participants, true); participants.className = 'sm-edit-event-participants';
            const evidence = textInput(event.evidence, 2000, 'Exact text copied from the scene prompt'); evidence.className = 'sm-edit-event-evidence';
            fields.append(controlLabel('Description', description), controlLabel('Type', mode),
                controlLabel('Participants · select all involved', participants), controlLabel('Exact source text', evidence));
            const actions = document.createElement('div'); actions.className = 'sm-actions';
            actions.append(editorButton('Move up', () => mutateEditor('events', index, 'up')),
                editorButton('Move down', () => mutateEditor('events', index, 'down')),
                editorButton('Remove', () => mutateEditor('events', index, 'remove'), true));
            row.append(fields, actions); eventContainer.append(row);
        });
    }

    function collectEditor() {
        const questions = promptReview?.preview.extraction.questions || [];
        const entities = [...document.querySelectorAll('.sm-edit-entity')].map(row => {
            const previous = draftExtraction.entities[Number(row.dataset.index)];
            return { ref: previous.ref, name: row.querySelector('.sm-edit-entity-name').value.trim(), kind: row.querySelector('.sm-edit-entity-kind').value };
        });
        const changes = [...document.querySelectorAll('.sm-edit-change')].map(row => ({
            subject: row.querySelector('.sm-edit-change-subject').value,
            property: row.querySelector('.sm-edit-change-property').value.trim(),
            value: row.querySelector('.sm-edit-change-value').value.trim(),
            target: row.querySelector('.sm-edit-change-target').value,
            remove: row.querySelector('.sm-edit-change-remove').checked,
            evidence: row.querySelector('.sm-edit-change-evidence').value
        }));
        const events = [...document.querySelectorAll('.sm-edit-event')].map(row => ({
            description: row.querySelector('.sm-edit-event-description').value.trim(),
            participants: [...row.querySelector('.sm-edit-event-participants').selectedOptions].map(option => option.value),
            evidence: row.querySelector('.sm-edit-event-evidence').value,
            mode: row.querySelector('.sm-edit-event-mode').value
        }));
        return { entities, changes, questions, events };
    }

    function mutateEditor(section, index, operation) {
        draftExtraction = collectEditor();
        const rows = draftExtraction[section];
        if (section === 'entities' && operation === 'remove') {
            const ref = rows[index].ref;
            if (draftExtraction.changes.some(c => c.subject === ref || c.target === ref || c.value === ref) ||
                draftExtraction.events.some(e => e.participants.includes(ref))) {
                el('sm-editor-status').textContent = 'This entity is used by changes or story moments. Exclude its card to remove its dependent changes together.';
                return;
            }
        }
        if (operation === 'remove') rows.splice(index, 1);
        else {
            const destination = operation === 'up' ? index - 1 : index + 1;
            if (destination < 0 || destination >= rows.length) return;
            [rows[index], rows[destination]] = [rows[destination], rows[index]];
        }
        markEditorDirty(); renderEditor();
    }

    function addEditorRow(section) {
        draftExtraction = collectEditor();
        const choices = editorChoices();
        if (section === 'entities') draftExtraction.entities.push({ ref: `user_${crypto.randomUUID().replaceAll('-', '')}`, name: '', kind: 'Other' });
        if (section === 'changes') draftExtraction.changes.push({ subject: choices[0]?.id || '', property: '', value: '', target: '', remove: false, evidence: '' });
        if (section === 'events') draftExtraction.events.push({ description: '', participants: choices[0] ? [choices[0].id] : [], evidence: '', mode: 'occurred' });
        markEditorDirty(); renderEditor();
    }

    const friendlyProperties = {
        held_by: 'Carried by', carried_at: 'Carrying position', located_at: 'Location', in: 'Inside', on: 'On',
        health: 'Health', life_state: 'Life state', status: 'State', disposition: 'Whereabouts',
        activity: 'Activity', action: 'Activity', color: 'Color', appearance: 'Appearance', condition: 'Condition',
        owned_by: 'Owned by', consumed_by: 'Consumed by', destroyed_by: 'Destroyed by'
    };
    const propertyName = key => friendlyProperties[key] || (key.startsWith('ability_') ? `Can ${key.slice(8).replaceAll('_', ' ')}` : key.replaceAll('_', ' '));
    const factValue = fact => !fact ? 'Not recorded' : fact.removed ? (fact.property === 'held_by' ? 'Not carried' : 'No longer set') :
        fact.property.startsWith('ability_') && ['true', 'false'].includes(fact.value) ? (fact.value === 'true' ? 'Yes' : 'No') : fact.value;
    const possessionAliases = ['has', 'holds', 'holding', 'carries', 'carrying'];
    const relationProperties = ['held_by', 'located_at', 'in', 'on', 'owned_by', 'consumed_by', 'destroyed_by'];

    function node(tag, text, className) {
        const result = document.createElement(tag);
        if (text !== undefined) result.textContent = text;
        if (className) result.className = className;
        return result;
    }

    // Proposed IDs for newly discovered entities are regenerated on each validation.
    // Resolve local references by name; never send a temporary proposed ID back in an edit.
    function resolveEntity(reference) {
        const declared = draftExtraction.entities.find(e => e.ref === reference);
        return promptReview.proposed.entities.find(e => e.id === reference ||
            e.name.toLowerCase() === (declared?.name || reference || '').trim().toLowerCase());
    }

    function changesFor(entity) {
        return draftExtraction.changes.map((change, index) => ({ change, index })).filter(({ change }) =>
            resolveEntity(possessionAliases.includes(change.property) || change.property === 'owns' ? change.target || change.value : change.subject)?.id === entity.id);
    }

    function why(container, evidence) {
        const snippets = [...new Set(evidence.filter(Boolean))];
        if (!snippets.length) return;
        const details = node('details'); details.append(node('summary', 'Why this change?'));
        snippets.forEach(text => details.append(node('blockquote', text)));
        container.append(details);
    }

    function renderCards() {
        const container = el('sm-change-cards'); container.replaceChildren();
        const entries = promptReview.proposed.history.slice(workspace.history.length).filter(f => !f.isEvent);
        const oldFacts = new Map();
        workspace.history.filter(f => !f.isEvent).forEach(f => oldFacts.set(`${f.entityId}:${f.property}`, f));
        promptReview.proposed.entities.forEach(entity => {
            const isNew = !workspace.entities.some(e => e.id === entity.id);
            const facts = entries.filter(f => f.entityId === entity.id);
            if (!isNew && !facts.length) return;
            const card = node('article', undefined, 'sm-card'); card.setAttribute('aria-label', `${entity.name} changes`);
            card.append(node('h4', entity.name), node('span', `${entity.kind}${isNew ? ' · New' : ''}`, 'sm-badge'));
            const final = new Map(); facts.forEach(f => final.set(f.property, f));
            if (!final.size) card.append(node('p', 'Discovered in your prompt. No new details yet.', 'sm-hint'));
            const automatic = [];
            final.forEach((fact, property) => {
                const before = oldFacts.get(`${entity.id}:${property}`);
                const row = node('div', undefined, 'sm-delta');
                const left = node('div'); left.append(node('small', `${propertyName(property)} · Before`), node('span', factValue(before)));
                const right = node('div'); right.append(node('small', 'After'), node('strong', factValue(fact)));
                row.append(left, node('span', '→'), right); card.append(row);
                if (fact.removed && !changesFor(entity).some(({ change }) => change.property === property && change.remove))
                    automatic.push(`${propertyName(property)} ends automatically.`);
            });
            if (automatic.length) {
                const details = node('details'); details.append(node('summary', 'Also changes automatically'));
                automatic.forEach(text => details.append(node('p', text))); card.append(details);
            }
            // Show ordering only when this prompt changes the same property more than once.
            const repeated = [...new Set(facts.map(f => f.property))].filter(p => facts.filter(f => f.property === p).length > 1);
            if (repeated.length) {
                const details = node('details'); details.append(node('summary', 'First → Then'));
                repeated.forEach(p => details.append(node('p', `${propertyName(p)}: ${facts.filter(f => f.property === p).map(factValue).join(' → ')}`)));
                card.append(details);
            }
            why(card, facts.map(f => f.note));
            if (!isNew && !changesFor(entity).length) {
                card.append(node('p', 'Updated automatically by another card. Edit that change to adjust this result.', 'sm-hint'));
                container.append(card); return;
            }
            const actions = node('div', undefined, 'sm-actions');
            const edit = editorButton('Edit', () => openEntityEditor(entity));
            const exclude = editorButton('Exclude', () => excludeEntity(entity), true);
            edit.disabled = exclude.disabled = guidedOpen || editorDirty;
            actions.append(edit, exclude); card.append(actions); container.append(card);
        });
        if (!container.children.length) emptyEditor(container, 'No changes to characters, objects or places. Review any story moments below.');
        const events = el('sm-event-cards'); events.replaceChildren();
        el('sm-story-events-title').textContent = `Story moments (${draftExtraction.events.length})`;
        draftExtraction.events.forEach((event, index) => {
            const card = node('article', undefined, 'sm-card');
            card.setAttribute('aria-label', `Story moment ${index + 1}`);
            const mode = { occurred: 'Happened', ongoing: 'In progress', intended: 'Planned', hypothetical: 'Imagined', negated: 'Did not happen' }[event.mode];
            card.append(node('span', mode, 'sm-badge'), node('h4', event.description),
                node('p', event.participants.map(ref => resolveEntity(ref)?.name || ref).join(', '), 'sm-hint'));
            why(card, [event.evidence]);
            const actions = node('div', undefined, 'sm-actions');
            const edit = editorButton('Edit moment', () => openEventEditor(index));
            const exclude = editorButton('Exclude moment', () => run(async () => {
                const plan = clone(draftExtraction); plan.events.splice(index, 1); await checkPlan(plan);
            }), true);
            edit.disabled = exclude.disabled = guidedOpen || editorDirty;
            actions.append(edit, exclude); card.append(actions); events.append(card);
        });
    }

    async function checkPlan(plan) {
        el('sm-editor-status').textContent = 'Checking the updated scene…';
        try {
            const review = await api(`${url()}/prompts/edit`, { previewId: promptReview.preview.id, extraction: plan });
            showPreview(review, true);
        } catch (error) {
            el('sm-editor-status').textContent = `Could not update the preview: ${error.message}`;
            if (el('sm-guided-feedback')) el('sm-guided-feedback').textContent = `Please correct this before continuing: ${error.message}`;
            throw error;
        }
    }

    function excludeEntity(entity) {
        run(async () => {
            const plan = clone(draftExtraction);
            const affected = new Set(changesFor(entity).map(item => item.index));
            const isNew = !workspace.entities.some(e => e.id === entity.id);
            const references = new Set(plan.entities.filter(e => resolveEntity(e.ref)?.id === entity.id).map(e => e.ref));
            references.add(entity.id);
            plan.changes = plan.changes.filter((c, i) => !affected.has(i) && (!isNew ||
                ![c.subject, c.target, c.value].some(ref => references.has(ref))));
            // Excluding a card also excludes moments involving it, avoiding contradictory
            // narration such as a discarded pen whose discard was excluded from state.
            plan.events = plan.events.filter(e => !e.participants.some(ref => references.has(ref)));
            if (isNew) plan.entities = plan.entities.filter(e => !references.has(e.ref));
            await checkPlan(plan);
            el('sm-editor-status').textContent = `${entity.name}: changes and related story moments excluded. Restore original to undo.`;
        });
    }

    function beginGuided(title) {
        editorReturnLabel = document.activeElement.closest('article')?.getAttribute('aria-label');
        guidedOpen = true;
        el('sm-advanced-editor').open = false;
        // Prevent simultaneous editing through two representations.
        el('sm-advanced-editor').hidden = true;
        el('sm-edit-dialog-title').textContent = title;
        const panel = el('sm-guided-editor'); panel.replaceChildren();
        el('sm-guided-actions').replaceChildren();
        renderCards(); availability();
        return panel;
    }

    function closeGuided() {
        const wasOpen = guidedOpen;
        guidedOpen = false;
        if (el('sm-edit-dialog').open) el('sm-edit-dialog').close();
        restoreEditorFocus = wasOpen;
        return wasOpen;
    }

    function cancelGuided() {
        if (busy || !guidedOpen) return;
        el('sm-message').textContent = '';
        showPreview(promptReview, true);
        el('sm-editor-status').textContent = 'Editing cancelled. The preview is unchanged.';
    }

    el('sm-close-editor').addEventListener('click', cancelGuided);
    el('sm-edit-dialog').addEventListener('cancel', event => {
        event.preventDefault();
        cancelGuided();
    });

    function finishGuided(panel, readPlan) {
        const actions = el('sm-guided-actions');
        const done = editorButton('Done', () => run(async () => { await checkPlan(readPlan()); }));
        done.className = '';
        const cancel = editorButton('Cancel', cancelGuided);
        const feedback = node('p', '', 'sm-questions'); feedback.id = 'sm-guided-feedback'; feedback.setAttribute('role', 'alert'); panel.append(feedback);
        actions.append(cancel, done);
        el('sm-edit-dialog').showModal();
        panel.querySelector('input, select, button')?.focus();
    }

    function openEntityEditor(entity) {
        if (editorDirty || guidedOpen || busy) return;
        const plan = clone(draftExtraction);
        const panel = beginGuided(`Edit ${entity.name}`);
        const declaration = plan.entities.find(e => resolveEntity(e.ref)?.id === entity.id);
        const isNew = !workspace.entities.some(e => e.id === entity.id);
        let name, kind;
        if (isNew && declaration) {
            name = textInput(entity.name, 100); kind = selectInput(kinds.map(k => ({ id: k, name: k })), entity.kind);
            const fields = node('div', undefined, 'sm-editor-fields'); fields.append(controlLabel('Name', name), controlLabel('Type', kind)); panel.append(fields);
        }
        const readers = [];
        const changes = changesFor(entity);
        panel.append(node('p', 'Choose what happens. Done checks the result; Apply saves it to the story.', 'sm-hint'));
        const list = node('div', undefined, 'sm-editor-list'); panel.append(list);
        const ref = declaration?.ref || entity.id;
        const add = (change, index) => {
            const row = node('div', undefined, 'sm-editor-row');
            if (possessionAliases.includes(change.property)) change = { ...change, subject: ref, property: 'held_by', target: change.subject, value: '' };
            if (change.property === 'owns') change = { ...change, subject: ref, property: 'owned_by', target: change.subject, value: '' };
            if (!change.remove && relationProperties.includes(change.property)) {
                const related = resolveEntity(change.target || change.value);
                if (related) change = { ...change, target: draftExtraction.entities.find(e => resolveEntity(e.ref)?.id === related.id)?.ref || related.id };
            }
            const actionOptions = [
                ['held_by', 'Give / carry'], ['owned_by', 'Set owner'], ['located_at', 'Move to'], ['in', 'Put inside'], ['on', 'Place on'],
                ['drop', 'Drop / release'], ['disposition', 'Change whereabouts'], ['condition', 'Change condition'],
                ['health', 'Change health'], ['life_state', 'Change life state'], ['appearance', 'Change appearance'],
                ['ability', 'Set an ability'], ['other', 'Other change']
            ];
            const actionKey = change.remove && change.property === 'held_by' ? 'drop' : change.property.startsWith('ability_') ? 'ability' :
                actionOptions.some(([key]) => key === change.property) ? change.property : 'other';
            const action = selectInput(actionOptions.map(([id, name]) => ({ id, name })), actionKey);
            row.append(controlLabel('What happens?', action));
            const inputs = node('div', undefined, 'sm-editor-fields'); row.append(inputs);
            const attribute = textInput(change.property, 80, 'e.g. color');
            const ability = textInput(change.property.startsWith('ability_') ? change.property.slice(8) : '', 72, 'e.g. sing');
            const value = textInput(change.value, 1000, 'e.g. sick, red, repaired');
            const target = selectInput([{ id: '', name: 'Choose…' }, ...editorChoices()], change.target);
            const clear = document.createElement('input'); clear.type = 'checkbox'; clear.checked = change.remove; clear.addEventListener('change', markEditorDirty);
            const relation = document.createElement('input'); relation.type = 'checkbox'; relation.checked = !!change.target; relation.addEventListener('change', () => { markEditorDirty(); draw(); });
            const capable = selectInput([{ id: 'true', name: 'Can' }, { id: 'false', name: 'Cannot' }], change.value || 'true');
            function draw() {
                inputs.replaceChildren();
                const key = action.value;
                if (key === 'drop') { inputs.append(node('p', `${entity.name} will no longer be carried.`, 'sm-hint')); return; }
                if (key === 'ability') { inputs.append(controlLabel('Ability', ability), controlLabel('Can do this?', capable)); return; }
                if (key === 'other') {
                    inputs.append(controlLabel('Detail to change', attribute));
                    const label = controlLabel('Link to someone or something', relation); label.className = 'sm-check'; inputs.append(label);
                }
                if (relationProperties.includes(key) || key === 'other' && relation.checked) inputs.append(controlLabel(`${entity.name} →`, target));
                else inputs.append(controlLabel('New value', value));
                const label = controlLabel('Clear this detail', clear); label.className = 'sm-check'; inputs.append(label);
            }
            action.addEventListener('change', () => { clear.checked = action.value === 'drop'; draw(); }); draw();
            const evidence = textInput(change.evidence, 2000, 'Copy the words that describe this change');
            const source = node('details'); source.append(node('summary', 'Why this change?'), controlLabel('Words from your prompt', evidence)); row.append(source);
            const include = document.createElement('input'); include.type = 'checkbox'; include.checked = true; include.addEventListener('change', markEditorDirty);
            const includeLabel = controlLabel('Include this change', include); includeLabel.className = 'sm-check'; row.append(includeLabel);
            list.append(row);
            readers.push(() => {
                if (!include.checked) return { index, change: null };
                const key = action.value;
                const property = key === 'drop' ? 'held_by' : key === 'ability' ? `ability_${ability.value.trim().toLowerCase().replaceAll(' ', '_')}` : key === 'other' ? attribute.value.trim() : key;
                const useTarget = key !== 'drop' && (relationProperties.includes(key) || key === 'other' && relation.checked);
                return { index, change: { subject: change.subject, property, value: useTarget || key === 'drop' ? '' : key === 'ability' ? capable.value : value.value.trim(),
                    target: useTarget ? target.value : '', remove: key === 'drop' || key !== 'ability' && clear.checked, evidence: evidence.value } };
            });
        };
        changes.forEach(({ change, index }) => add(change, index));
        panel.append(editorButton('Add a change', () => { add({ subject: ref, property: 'condition', value: '', target: '', remove: false, evidence: '' }, null); markEditorDirty(); }));
        finishGuided(panel, () => {
            const updated = clone(plan);
            if (name) { const identity = updated.entities.find(e => e.ref === declaration.ref); identity.name = name.value.trim(); identity.kind = kind.value; }
            const edits = readers.map(read => read());
            updated.changes = plan.changes.flatMap((c, i) => { const edit = edits.find(e => e.index === i); return edit ? edit.change ? [edit.change] : [] : [c]; });
            updated.changes.push(...edits.filter(e => e.index === null && e.change).map(e => e.change));
            return updated;
        });
    }

    function openEventEditor(index) {
        if (editorDirty || guidedOpen || busy) return;
        const plan = clone(draftExtraction), event = plan.events[index];
        const panel = beginGuided('Edit story moment');
        const description = textInput(event.description, 1000);
        const mode = selectInput(['Happened', 'In progress', 'Planned', 'Imagined', 'Did not happen'].map((name, i) => ({ id: eventModes[i], name })), event.mode);
        panel.append(controlLabel('What happens?', description), controlLabel('When / whether it happens', mode));
        const participants = editorChoices().map(choice => {
            const input = document.createElement('input'); input.type = 'checkbox'; input.checked = event.participants.includes(choice.id);
            input.addEventListener('change', markEditorDirty);
            const label = controlLabel(choice.name, input); label.className = 'sm-check'; panel.append(label);
            return { input, id: choice.id };
        });
        const evidence = textInput(event.evidence, 2000);
        const source = node('details'); source.append(node('summary', 'Why this change?'), controlLabel('Words from your prompt', evidence)); panel.append(source);
        finishGuided(panel, () => {
            plan.events[index] = { description: description.value, mode: mode.value, evidence: evidence.value, participants: participants.filter(p => p.input.checked).map(p => p.id) };
            return plan;
        });
    }

    function showPreview(review, preserveOriginal = false) {
        if (el('sm-prompt-diagnostics')) el('sm-prompt-diagnostics').hidden = true;
        promptReview = review;
        const plan = review.preview.extraction;
        if (!preserveOriginal) originalExtraction = clone(plan);
        draftExtraction = clone({ ...plan, events: plan.events || [] });
        // Some small models redundantly redeclare every existing identity by its real ID.
        // Those identities are fixed and already appear in all relation selectors, so only
        // genuinely new identities (or local aliases needed by the plan) belong in this editor.
        draftExtraction.entities = draftExtraction.entities.filter(entity =>
            !(workspace?.entities || []).some(existing => existing.id === entity.ref));
        editorDirty = false;
        const closedEditor = closeGuided();
        el('sm-advanced-editor').hidden = false;
        const added = review.proposed.entities.filter(e => !workspace.entities.some(old => old.id === e.id));
        const entries = review.proposed.history.slice(workspace.history.length);
        const changes = entries.filter(f => !f.isEvent), events = entries.filter(f => f.isEvent);
        const proposedNames = new Map(review.proposed.entities.map(e => [e.id, e.name]));
        el('sm-preview-summary').textContent = `${added.length} newly discovered · ${new Set(changes.map(f => f.entityId)).size} with changes · ${events.length} story moments`;
        el('sm-apply-prompt').textContent = `Apply ${changes.length + events.length} changes${!changes.length && !events.length && added.length ? ` · ${added.length} new entities` : ''}`;
        el('sm-prompt-questions').textContent = plan.questions.length ?
            `Clarification needed. Edit the prompt and preview again:\n${plan.questions.map(q => `• ${q}`).join('\n')}` : '';
        renderEditor();
        renderCards();
        el('sm-editor-status').textContent = preserveOriginal ? 'Changes checked. Review the updated cards, then apply.' : 'Ready to review. Your saved scene is unchanged.';
        const current = new Map();
        review.proposed.history.filter(f => !f.isEvent).forEach(f => current.set(`${f.entityId}:${f.property}`, f));
        table('sm-preview-state', ['Entity', 'Property', 'Result'], [...current.values()].filter(f => !f.removed).map(f =>
            [proposedNames.get(f.entityId), f.property.replaceAll('_', ' '), f.value]), 'No properties yet.');
        el('sm-preview-json').textContent = JSON.stringify(review.preview, null, 2);
        el('sm-prompt-preview').hidden = false;
        if (!closedEditor) el('sm-review-heading').focus();
        availability();
    }

    ['sm-prompt-text', 'sm-prompt-model', 'sm-prompt-label', 'sm-scene-label', 'sm-shot-duration'].forEach(id => el(id).addEventListener('input', () => {
        if (promptReview) el('sm-prompt-status').textContent = 'Prompt settings changed. Preview again before applying.';
        clearPreview(); availability();
    }));
    const examples = {
        discover: 'Maya enters the kitchen carrying a red mug. She places it on the counter.',
        transfer: 'Maya picks up the red mug from the counter and gives it to Alex. Alex walks into the garden carrying it.',
        ambiguity: 'There is a blue mug beside the red mug. Alex picks up the mug.'
    };
    Object.entries(examples).forEach(([key, text]) => el(`sm-example-${key}`).addEventListener('click', () => {
        clearPreview(); el('sm-prompt-text').value = text;
        el('sm-prompt-status').textContent = 'Example loaded. Preview it, then apply to continue the story.';
        availability();
    }));
    const situations = {
        discard: ["ActorA has a pen in his pocket, then throws it away. Later ActorD finds the pen and picks it up.", "Expect the pen's old possession and pocket to end, followed by recovery and possession by ActorD. Both events stay in history."],
        mortality: ["ActorB is walking through the garden. ActorB dies.", "Expect life state dead and walking to end. The character and their history remain. Death does not automatically erase possessions or destroy the garden."],
        ability: ["ActorC can sing and dance but got sick. ActorC is too sick to sing today.", "Expect separate singing and dancing abilities, health sick, and singing availability unable. Try a follow-up: ActorC recovers and can sing again."],
        place: ["Maya enters the kitchen. Rain starts in the garden. The kitchen lights go out and the kitchen door becomes locked.", "Expect location, weather, lighting and door lock state to be tracked separately. No unsupported changes to other places or people."],
        object: ["Alex carries a phone. Its screen cracks, but the phone is still powered on. Alex repairs the screen, then turns the phone off.", "Expect possession to remain, screen condition to change through damage and repair, and power to end off."],
        intention: ["Maya holds a cup. Maya plans to drop the cup but does not drop it.", "Expect Maya to keep the cup. The plan and negation belong in events without a completed transfer."]
    };
    el('sm-example-scenario').addEventListener('change', event => {
        const example = situations[event.target.value];
        if (!example) return;
        clearPreview(); el('sm-prompt-text').value = example[0];
        el('sm-example-expectation').textContent = example[1];
        el('sm-prompt-status').textContent = 'Example loaded. Preview and review the effects before applying.';
        availability();
    });
    el('sm-add-preview-entity').addEventListener('click', () => addEditorRow('entities'));
    el('sm-add-preview-change').addEventListener('click', () => addEditorRow('changes'));
    el('sm-add-preview-event').addEventListener('click', () => addEditorRow('events'));
    el('sm-save-corrections').addEventListener('click', () => run(async () => {
        draftExtraction = collectEditor();
        el('sm-editor-status').textContent = 'Validating corrections against the prompt and current scene…';
        try {
            const review = await api(`${url()}/prompts/edit`, {
                previewId: promptReview.preview.id,
                extraction: draftExtraction
            });
            showPreview(review, true);
            el('sm-prompt-status').textContent = 'Corrections saved. Review the resulting state and apply it when ready.';
        } catch (error) {
            el('sm-editor-status').textContent = `Correction blocked: ${error.message}`;
            showPromptDiagnostics(error.diagnostics, promptReview.preview.prompt);
            throw error;
        }
    }));
    el('sm-reset-corrections').addEventListener('click', () => run(async () => {
        el('sm-editor-status').textContent = 'Restoring and validating the original model interpretation…';
        try {
            const review = await api(`${url()}/prompts/edit`, {
                previewId: promptReview.preview.id,
                extraction: clone(originalExtraction)
            });
            showPreview(review, true);
            el('sm-prompt-status').textContent = 'Original model interpretation restored. Nothing has been applied to the scene.';
        } catch (error) {
            el('sm-editor-status').textContent = `Restore blocked: ${error.message}`;
            throw error;
        }
    }));
    el('sm-refresh-models').addEventListener('click', () => run(models));
    el('sm-discard-prompt').addEventListener('click', () => {
        clearPreview(); el('sm-prompt-status').textContent = 'Preview discarded. Saved memory is unchanged.'; availability();
    });
    el('sm-cancel-interpret').addEventListener('click', () => promptAbortController?.abort());
    form('sm-prompt-form', async data => {
        clearPreview();
        data.durationSeconds = data.durationSeconds ? Number(data.durationSeconds) : null;
        const controller = new AbortController();
        startPromptProgress(controller);
        el('sm-prompt-status').textContent = 'Creating a preview. Your saved scene is unchanged.';
        try {
            showPreview(await streamPromptPreview(data, controller.signal));
            el('sm-prompt-status').textContent = 'Review the proposed changes below. Edit the prompt to correct an interpretation.';
        } catch (error) {
            if (error.name === 'AbortError') {
                el('sm-prompt-status').textContent = 'Preview cancelled. Saved memory is unchanged.';
                return;
            }
            el('sm-prompt-status').textContent = error.message;
            showPromptDiagnostics(error.diagnostics, data.prompt);
            throw error;
        } finally { stopPromptProgress(); }
    });
    el('sm-apply-prompt').addEventListener('click', () => run(async () => {
        const id = promptReview.preview.id;
        workspace = await api(`${url()}/prompts/apply`, { previewId: id });
        render();
        el('sm-prompt-status').textContent = 'Applied. The scene and history below are updated. Write the next prompt to continue the story.';
        el('sm-direct-result').textContent = 'Memory changed. Select an entity and query its saved state below.';
        el('sm-agent-result').textContent = 'Memory changed. Ask the agent again to see the latest facts.';
    }));

    el('sm-workspace').addEventListener('change', () => run(() => loadWorkspace(el('sm-workspace').value)));
    form('sm-create-workspace', async data => {
        const created = await api('/api/scene-memory/workspaces', data);
        el('sm-create-workspace').reset();
        await listWorkspaces(created.id);
    });
    form('sm-add-entity', async data => {
        workspace = await api(`${url()}/entities`, data);
        el('sm-add-entity').reset(); render();
        el('sm-fact-entity').value = workspace.entities.at(-1).id;
    });
    form('sm-record-fact', async data => {
        workspace = await api(`${url()}/facts`, data); render();
        el('sm-direct-result').textContent = 'Memory changed. Query again to see the latest facts.';
        el('sm-agent-result').textContent = 'Memory changed. Ask the agent again to see the latest facts.';
    });
    el('sm-holder-object').addEventListener('change', holderPreview);
    el('sm-holder-character').addEventListener('change', holderPreview);
    form('sm-set-holder', async data => {
        workspace = await api(`${url()}/holder`, data); render();
        el('sm-query-entity').value = data.objectId;
        result('sm-direct-result', await api(`${url()}/query`, { entityId: data.objectId }));
        el('sm-agent-result').textContent = 'Possession changed. Ask the agent again to see the latest facts.';
    });
    form('sm-direct-query', async data => result('sm-direct-result', await api(`${url()}/query`, query(data))));
    form('sm-create-agent', async data => {
        const agent = await api(`${url()}/agents`, data);
        agents = await api(`${url()}/agents`);
        options('sm-agent', agents); el('sm-agent').value = agent.id;
        el('sm-create-agent').reset();
        el('sm-agent-result').textContent = `${agent.name} is ready. Ask about an entity in this workspace.`;
    });
    form('sm-agent-query', async data => {
        const response = await api(`${url()}/agents/${encodeURIComponent(data.agentId)}/query`, query(data));
        result('sm-agent-result', response.result, 'Agent → Scene Memory tool → saved facts\n\n');
    });
    run(async () => { await listWorkspaces(new URL(location.href).searchParams.get('workspaceId')); await models(); });
})();
