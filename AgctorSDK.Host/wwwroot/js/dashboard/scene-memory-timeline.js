(() => {
    'use strict';
    const el = id => document.getElementById(id);
    let workspace = null, selectedIndex = 0;

    async function api(path, options) {
        const response = await fetch(path, options); const text = await response.text(); const data = text ? JSON.parse(text) : {};
        if (!response.ok) throw new Error(data.error || data.message || data.title || Object.values(data.errors || {}).flat().join(' ') || 'Request failed.');
        return data;
    }
    function node(tag, text, className) { const n=document.createElement(tag); if(text!==undefined)n.textContent=text; if(className)n.className=className; return n; }
    const prompts = () => workspace?.prompts || [];
    const entity = id => workspace.entities.find(e => e.id === id);
    function shotPresentation(shot,index) {
        // Use an explicit prompt heading as a display title; saved metadata stays unchanged.
        const prompt=(shot.prompt||'').trim(), firstLine=prompt.split(/\r?\n/,1)[0];
        const timedHeading=firstLine.match(/^\d{1,2}:\d{2}\s*[–—-]\s*\d{1,2}:\d{2}\s*[—–-]\s*(.+)$/);
        const hasHeading=firstLine.length<=120 && /\r?\n\s*\r?\n/.test(prompt.slice(firstLine.length,firstLine.length+6)) && (timedHeading || (firstLine===firstLine.toUpperCase() && /[A-Z]/.test(firstLine)));
        return {title:shot.label || (hasHeading ? timedHeading?.[1] || firstLine : `Shot ${index+1}`), excerpt:hasHeading ? prompt.slice(firstLine.length).trim() : prompt};
    }
    const shotName = (shot,index) => shotPresentation(shot,index).title;
    const sceneName = shot => shot.sceneLabel || 'Unassigned scene';
    function formatDuration(seconds) { if (!seconds) return 'Duration not set'; const minutes=Math.floor(seconds/60), rest=Math.round((seconds%60)*10)/10; return minutes ? `${minutes}m ${rest ? `${rest}s` : ''}`.trim() : `${rest}s`; }
    function groupScenes(items) {
        const groups=[]; items.forEach((shot,index) => { const name=sceneName(shot), previous=groups.at(-1); if(!previous || previous.name!==name) groups.push({name,shots:[]}); groups.at(-1).shots.push({shot,index}); }); return groups;
    }
    function currentFacts(sequence) {
        const map=new Map(); workspace.history.filter(f => !f.isEvent && f.sequence <= sequence).forEach(f => map.set(`${f.entityId}:${f.property.toLowerCase()}`,f)); return map;
    }
    function stateBefore(shot) { return currentFacts(Math.max(0,shot.firstSequence-1)); }
    function stateAfter(shot) { return currentFacts(Math.max(0,shot.lastSequence)); }
    function knownEntityIds(index, sequence) {
        const ids=new Set();
        workspace.history.filter(f => f.sequence <= sequence).forEach(f => { ids.add(f.entityId); (f.participantIds||[]).forEach(id=>ids.add(id)); if(f.relatedEntityId)ids.add(f.relatedEntityId); });
        prompts().slice(0,index+1).forEach(p => (p.introducedEntityIds||[]).forEach(id=>ids.add(id)));
        workspace.entities.filter(e => !workspace.history.some(f => f.entityId===e.id || f.relatedEntityId===e.id || (f.participantIds||[]).includes(e.id))).forEach(e=>ids.add(e.id));
        return ids;
    }
    function renderMetrics(groups) {
        el('smt-metrics').hidden=false; el('smt-scenes').textContent=groups.length; el('smt-shots').textContent=prompts().length; el('smt-changes').textContent=workspace.history.length;
        const durations=prompts().map(p=>p.durationSeconds).filter(Boolean), unknown=prompts().length-durations.length, total=durations.reduce((a,b)=>a+b,0);
        el('smt-runtime').textContent = total
            ? `${formatDuration(total)}${unknown ? ` · ${unknown} unsized` : ''}`
            : (unknown ? `Not set · ${unknown} shot${unknown === 1 ? '' : 's'}` : '—');
        const manual = workspace.history.filter(f => !f.promptId).length;
        el('smt-baseline').hidden = !manual;
        el('smt-baseline').textContent = manual ? `${manual} manual/setup ${manual === 1 ? 'entry is' : 'entries are'} included in state snapshots but not shown as shot cards.` : '';
    }
    function renderTimeline() {
        const list=prompts(), groups=groupScenes(list); renderMetrics(groups);
        el('smt-story').hidden=!list.length; el('smt-detail').hidden=!list.length; el('smt-empty').hidden=!!list.length;
        if(!list.length)return; selectedIndex=Math.max(0,Math.min(selectedIndex,list.length-1));
        const target=el('smt-timeline'); target.replaceChildren(); groups.forEach(group => {
            const section=node('section',undefined,'scene'), title=node('div',undefined,'scene-title'), sceneLabel=node('span',group.name,'scene-name'), row=node('div',undefined,'shot-row');
            sceneLabel.title=group.name;
            title.append(sceneLabel,node('span',`${group.shots.length} shot${group.shots.length===1?'':'s'}`,'scene-count'));
            group.shots.forEach(({shot,index}) => {
                const presentation=shotPresentation(shot,index), button=node('button',undefined,'shot'), top=node('span',undefined,'shot-top'), footer=node('span',undefined,'shot-footer');
                button.type='button'; button.title=presentation.title;
                button.setAttribute('aria-pressed',String(index===selectedIndex));
                button.setAttribute('aria-label',`Shot ${index+1}: ${presentation.title}, ${group.name}`);
                top.append(node('span',`SHOT ${String(index+1).padStart(2,'0')}`,'shot-number'));
                if(index===selectedIndex) top.append(node('span','Viewing','shot-selected'));
                const moments=workspace.history.filter(f=>f.promptId===shot.id && f.isEvent).length;
                footer.append(node('span',formatDuration(shot.durationSeconds),'shot-duration'),node('span',`${moments} moment${moments===1?'':'s'}`));
                button.append(top,node('strong',presentation.title),node('span',presentation.excerpt,'shot-excerpt'),footer);
                button.addEventListener('click',()=>select(index)); row.append(button);
            });
            section.append(title,row); target.append(section);
        }); renderDetail();
    }
    function select(index) {
        const restoreFocus=el('smt-timeline').contains(document.activeElement);
        selectedIndex=index; renderTimeline();
        const selected=document.querySelectorAll('.smt .shot')[selectedIndex];
        if(restoreFocus)selected?.focus({preventScroll:true});
        selected?.scrollIntoView({behavior:matchMedia('(prefers-reduced-motion: reduce)').matches?'auto':'smooth',block:'nearest',inline:'center'});
    }
    function renderDetail() {
        const shot=prompts()[selectedIndex], before=stateBefore(shot), after=stateAfter(shot), shotFacts=workspace.history.filter(f=>f.promptId===shot.id && !f.isEvent), changedKeys=[...new Set(shotFacts.map(f=>`${f.entityId}:${f.property.toLowerCase()}`))];
        el('smt-position').textContent=`${sceneName(shot)} · Shot ${selectedIndex+1} of ${prompts().length}`; el('smt-shot-title').textContent=shotName(shot,selectedIndex); el('smt-prompt').textContent=shot.prompt;
        const meta=el('smt-shot-meta'); meta.replaceChildren(); [shot.model,formatDuration(shot.durationSeconds),shot.lastSequence>=shot.firstSequence?`Changes #${shot.firstSequence}–#${shot.lastSequence}`:'No state entries'].forEach(v=>meta.append(node('span',v,'pill')));
        const deltas=el('smt-deltas'); deltas.replaceChildren();
        if(!changedKeys.length)deltas.append(node('p','No state properties changed. This may be an event-only or entity-only shot.'));
        changedKeys.forEach(key => { const [entityId,property]=key.split(':'), old=before.get(key), next=after.get(key), row=node('div',undefined,'delta'); row.append(node('div',`${entity(entityId)?.name||entityId} · ${property.replaceAll('_',' ')}`,'entity'),node('div',old&&!old.removed?old.value:'Not set','before'),node('div','→','arrow'),node('div',next&&!next.removed?next.value:'Cleared','after')); deltas.append(row); });
        const state=el('smt-state'); state.replaceChildren(); const ids=knownEntityIds(selectedIndex,Math.max(0,shot.lastSequence)); const facts=[...after.values()].filter(f=>!f.removed&&ids.has(f.entityId));
        [...ids].map(entity).filter(Boolean).sort((a,b)=>a.name.localeCompare(b.name)).forEach(item => { const card=node('article',undefined,'entity-card'); const title=node('h4',item.name); title.append(node('small',` · ${item.kind}`)); card.append(title); const own=facts.filter(f=>f.entityId===item.id); if(!own.length)card.append(node('p','No current properties.')); else { const dl=document.createElement('dl'); own.forEach(f=>{const row=node('div');row.append(node('dt',f.property.replaceAll('_',' ')),node('dd',f.value));dl.append(row);});card.append(dl);} state.append(card); });
        const introduced=el('smt-introduced'); introduced.replaceChildren(); const introducedIds=shot.introducedEntityIds||[...new Set(shotFacts.flatMap(f=>[f.entityId,f.relatedEntityId]).filter(Boolean))].filter(id=>!workspace.history.some(f=>f.sequence<shot.firstSequence&&(f.entityId===id||f.relatedEntityId===id)));
        introducedIds.map(entity).filter(Boolean).forEach(item=>introduced.append(node('li',`${item.name} · ${item.kind}`))); if(!introduced.children.length)introduced.append(node('li','No new entities recorded.'));
        const events=el('smt-events'); events.replaceChildren(); workspace.history.filter(f=>f.promptId===shot.id&&f.isEvent).forEach(event=>events.append(node('li',`[${event.eventMode}] ${event.value}`))); if(!events.children.length)events.append(node('li','No story moments recorded.'));
        el('smt-previous').disabled=selectedIndex===0; el('smt-next').disabled=selectedIndex===prompts().length-1;
        el('smt-story-position').textContent=`${selectedIndex+1} / ${prompts().length}`;
    }
    async function loadWorkspace(id) { el('smt-error').textContent=''; if(!id){workspace=null;return;} try { workspace=await api(`/api/scene-memory/workspaces/${encodeURIComponent(id)}`); selectedIndex=0; const memoryUrl=`/Dashboard/SceneMemory?workspaceId=${encodeURIComponent(id)}`; el('smt-edit-memory').href=memoryUrl; el('smt-create-shot').href=memoryUrl; renderTimeline(); const url=new URL(location.href); url.searchParams.set('workspaceId',id); history.replaceState(null,'',url); } catch(error){el('smt-error').textContent=error.message;} }
    async function init() {
        try { const list=await api('/api/scene-memory/workspaces'); const select=el('smt-workspace'); select.replaceChildren(); if(!list.length){select.add(new Option('No workspaces yet','')); el('smt-empty').hidden=false; return;} list.forEach(w=>select.add(new Option(`${w.name} · ${w.changeCount} changes`,w.id))); const requested=new URL(location.href).searchParams.get('workspaceId'); if(requested&&list.some(w=>w.id===requested))select.value=requested; await loadWorkspace(select.value); } catch(error){el('smt-error').textContent=error.message;}
    }
    function openEditor() { const shot=prompts()[selectedIndex], form=el('smt-edit-form'); form.elements.sceneLabel.value=shot.sceneLabel||''; form.elements.shotLabel.value=shot.label||''; form.elements.durationSeconds.value=shot.durationSeconds||''; el('smt-edit-dialog').showModal(); }
    function closeEditor(){el('smt-edit-dialog').close();}
    el('smt-workspace').addEventListener('change',e=>loadWorkspace(e.target.value)); el('smt-previous').addEventListener('click',()=>select(selectedIndex-1)); el('smt-next').addEventListener('click',()=>select(selectedIndex+1)); el('smt-edit').addEventListener('click',openEditor); el('smt-edit-close').addEventListener('click',closeEditor); el('smt-edit-cancel').addEventListener('click',closeEditor);
    el('smt-edit-form').addEventListener('submit',async event=>{event.preventDefault(); const form=new FormData(event.currentTarget), shot=prompts()[selectedIndex]; try { workspace=await api(`/api/scene-memory/workspaces/${encodeURIComponent(workspace.id)}/prompts/${encodeURIComponent(shot.id)}/metadata`,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify({sceneLabel:form.get('sceneLabel'),shotLabel:form.get('shotLabel'),durationSeconds:form.get('durationSeconds')?Number(form.get('durationSeconds')):null})}); closeEditor(); renderTimeline(); } catch(error){el('smt-error').textContent=error.message;closeEditor();} });
    init();
})();
