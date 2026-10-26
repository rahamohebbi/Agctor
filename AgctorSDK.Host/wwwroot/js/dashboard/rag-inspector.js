(function (global) {
    'use strict';
    var drawer = document.getElementById('pm-rag-inspector');
    if (!drawer) return;
    var picker = drawer.querySelector('[data-rag-picker]'), panel = drawer.querySelector('[data-rag-panel]');
    var status = drawer.querySelector('[data-rag-status]'), subtitle = drawer.querySelector('[data-rag-subtitle]');
    var tabs = Array.from(drawer.querySelectorAll('[role=tab]'));
    var session = null, turn = null, selected = null, records = [], detail = null, tab = 'overview';
    var revision = 0, detailRevision = 0, setupRevision = 0, timer = null, live = false, liveTurn = null, returnFocus = null;
    var setup = null, setupError = null;
    var labels = { running:'Retrieving', success:'Retrieved', unavailable:'Provider unavailable', failed:'Failed', cancelled:'Cancelled', no_results:'No results', disabled:'External RAG disabled', not_requested:'Not requested', skipped:'Skipped' };
    function el(tag, text, cls) { var n = document.createElement(tag); if (text != null) n.textContent = text; if (cls) n.className = cls; return n; }
    function pre(text) { return el('pre', text || 'Not reported'); }
    function note(text, warn) { return el('div', text, 'rag-note' + (warn ? ' warn' : '')); }
    function isWarning(r) { return r.markdownFallback || ['failed','unavailable','cancelled','no_results'].includes(r.outcome); }
    function duration(ms) { return ms >= 1000 ? (ms / 1000).toFixed(2) + ' s' : Math.round(ms || 0) + ' ms'; }
    function endpoint(id) { return '/api/chat/sessions/' + encodeURIComponent(session) + '/rag' + (id ? '/' + encodeURIComponent(id) : ''); }
    async function read(url) { var res = await fetch(url); if (!res.ok) throw new Error('Retrieval diagnostics could not be loaded (' + res.status + ').'); return res.json(); }
    function summary(r) { return r.providerId + ' · ' + (labels[r.outcome] || r.outcome) + (r.markdownFallback ? ' · Markdown fallback' : '') + ' · ' + r.resultCount + ' results · ' + duration(r.durationMs); }
    function empty(title, message) { var box = el('div', null, 'rag-empty'); box.append(el('strong',title),el('p',message,'rag-muted')); panel.replaceChildren(box); }
    function section(title, content) { panel.append(el('h3',title),content); }
    function stat(label, value) { var box=el('div',null,'rag-stat'); box.append(el('span',label,'rag-muted'),el('strong',value == null ? 'Not reported' : String(value))); return box; }
    function copyButton(text, label) {
        var b=el('button',label || 'Copy','rag-small'); b.type='button'; b.onclick=async function(){try {await navigator.clipboard.writeText(text);status.textContent='Copied.';}catch(_){status.textContent='Copy unavailable. Select and copy the text below.';}}; return b;
    }
    function setupCard() {
        var card=el('section',null,'rag-setup');
        card.append(el('h3','Retrieval readiness'));
        if(setupError){card.append(note(setupError,true));return card;}
        if(!setup){card.append(el('p','Loading provider and transfer status…','rag-muted'));return card;}
        var grid=el('div',null,'rag-grid');
        grid.append(stat('Effective provider',(setup.provider&&setup.provider.id)||'None'),stat('Selected by',(setup.provider&&setup.provider.source)||'Not configured'));
        card.append(grid);
        var transfer=setup.latestTransfer;
        if(transfer){
            var transferText=transfer.providerId+' · '+transfer.status+' · '+transfer.accepted+' accepted';
            card.append(el('h3','Latest markdown transfer'),el('p',transferText,'rag-setup-line'));
            card.append(el('p',transfer.message,'rag-muted'));
        } else card.append(note('No markdown transfer is recorded for this scenario.',false));
        var steps=setup.retrievalSteps||[];
        if(steps.length){
            card.append(el('h3','RAG-enabled AI steps'));
            var list=el('ul',null,'rag-step-list');
            steps.forEach(function(s){list.append(el('li',(s.label||s.id)+' · '+(s.strategy==='graph_rag'?'Graph RAG':'Vector RAG')));});
            card.append(list,note('Send a new message that reaches one of these steps. Its query, results, and prepared context will appear here. Existing messages are not reconstructed.',false));
        } else {
            card.append(note('No AI step in this scenario uses Vector RAG or Graph RAG. Choosing a provider does not change a step that is configured for markdown context.',true));
            var link=el('a','Open scenario flow to enable RAG ↗','rag-small');link.href='/Dashboard/Scenarios';card.append(link);
        }
        return card;
    }
    function renderEmpty() {
        var box=el('div',null,'rag-empty rag-empty-ready');
        box.append(el('strong',turn?'No retrieval request for this message':'No retrieval requests recorded'));
        box.append(el('p',live?'Waiting for this message’s first RAG-enabled AI step…':'The inspector records searches made during new chat messages; importing files does not create a search record.','rag-muted'));
        panel.replaceChildren(box,setupCard());
    }
    function render() {
        tabs.forEach(function (b) { var active=b.dataset.ragTab===tab; b.setAttribute('aria-selected',String(active));b.tabIndex=active?0:-1; });
        panel.setAttribute('aria-labelledby','rag-tab-'+tab);
        panel.replaceChildren();
        drawer.querySelector('[data-rag-export]').disabled = !detail;
        drawer.querySelector('[data-rag-copy]').disabled = !detail;
        if (!detail) { renderEmpty(); return; }
        var r=detail;
        subtitle.textContent=(r.correlation.agentId || 'Agent') + (r.correlation.nodeId ? ' · '+r.correlation.nodeId : '') + ' · '+new Date(r.startedAt).toLocaleString();
        if (tab==='overview') {
            var grid=el('div',null,'rag-grid');grid.append(stat('Outcome',labels[r.outcome]||r.outcome),stat('Provider',r.providerId),stat('Selected by',r.providerSource),stat('Duration',r.completedAt?duration(r.durationMs):'In progress'));panel.append(grid);
            if(r.reason)panel.append(note(r.reason,isWarning(r)));
            if(r.markdownFallback)panel.append(note('External RAG did not supply the context. This step prepared on-disk markdown instead.',true));
            var stages=el('ol',null,'rag-stages');(r.stages||[]).forEach(function(s){var li=el('li');li.append(el('span',s.status==='running'?'◌':s.status==='complete'?'✓':'!','rag-stage-mark '+s.status),el('span',s.name),el('span',s.durationMs>0?duration(s.durationMs):s.status,'rag-muted'));if(s.detail)li.append(el('small',s.detail));stages.append(li);});section('Execution',stages);
            panel.append(el('p','Provider-internal embedding, graph traversal, and reranking stages: not reported unless included in the provider response.','rag-muted'));
            section('Request identity',pre(JSON.stringify({scenario:r.scenarioId,session:r.correlation.sessionId,turn:r.correlation.turnGroupId,node:r.correlation.nodeId,trace:r.correlation.traceId},null,2)));
        } else if (tab==='query') {
            section('Search text',pre(r.query));panel.append(copyButton(r.query,'Copy query'));
            var queryGrid=el('div',null,'rag-grid');queryGrid.append(stat('Collection',r.collectionId||'Provider default'),stat('Requested mode',r.mode),stat('Requested result limit',r.topK||'Not requested'),stat('Context strategy',r.strategy));section('Retrieval settings',queryGrid);
            panel.append(note(['not_requested','disabled','skipped'].includes(r.outcome) ? 'No external search was sent for this step.' : 'This is the search text supplied to the selected adapter. Provider-side query rewriting and filters are not reported by the current adapters.'));
        } else if(tab==='results') {
            panel.append(el('h3',r.resultCount+' results returned'));
            if(!r.results.length)panel.append(note(r.reason || 'No external results were returned for this step.',isWarning(r)));
            if(r.resultsPreviewTruncated)panel.append(note('Result previews are limited. The Context tab shows the separately captured prepared context.',true));
            r.results.forEach(function(c){var card=el('details',null,'rag-card');var heading=el('summary','#'+c.rank+' · '+(c.source||(c.metadata&&c.metadata.name)||'Source not reported'));card.append(heading);if(!c.source)card.append(el('div','Source path not reported','rag-source'));if(c.score!=null)card.append(el('div','Provider score: '+c.score+' (provider-specific)','rag-source'));card.append(el('p',c.text));if(c.previewTruncated)card.append(note('This result preview was truncated.',true));
                var actions=el('div',null,'rag-actions');actions.append(copyButton(c.text,'Copy result'));if(c.source){actions.append(copyButton(c.source,'Copy source'));try{var u=new URL(c.source);if(['http:','https:'].includes(u.protocol)){var a=el('a','Open source ↗','rag-small');a.href=u.href;a.target='_blank';a.rel='noopener noreferrer';actions.append(a);}}catch(_){}}
                card.append(actions);if(c.metadata&&Object.keys(c.metadata).length)card.append(pre(JSON.stringify(c.metadata,null,2)));panel.append(card);});
            panel.append(el('p','Returned results are retrieval evidence. They do not prove the model cited or relied on each result.','rag-muted'));
        } else if(tab==='context') {
            panel.append(el('h3','Prepared LLM context'));
            panel.append(el('p','The memory appendix prepared for this agent’s prompt, including instructions and any markdown fallback. This is not the full prompt or proof that generation completed.','rag-muted'));
            if(r.contextTruncated)panel.append(note('Memory context exceeded the prompt budget and was truncated before preparation.',true));
            if(r.contextPreviewTruncated)panel.append(note('The saved context preview exceeds the diagnostics limit and is truncated.',true));
            panel.append(el('p',r.preparedContext.length.toLocaleString()+' captured characters','rag-muted'),copyButton(r.preparedContext,'Copy context'),pre(r.preparedContext||'No context was prepared.'));
        } else {
            section('Provider trace ID',pre(r.providerTraceId||'Not reported'));
            section('Request',pre(JSON.stringify({provider:r.providerId,query:r.query,collectionId:r.collectionId,mode:r.mode,topK:r.topK},null,2)));
            var raw=el('details',null,'rag-card');raw.append(el('summary','Provider response JSON'));var value=r.rawResponseJson;try{value=JSON.stringify(JSON.parse(value),null,2);}catch(_){}raw.append(pre(value||'Not reported'));section('Response',raw);
            if(r.rawResponseTruncated)panel.append(note('Raw response omitted because it exceeded the diagnostics size limit.',true));
            panel.append(el('p','Credential fields are redacted. Copies and exports include retrieved content and queries.','rag-muted'));
        }
    }
    function updateBadges(list) {
        document.querySelectorAll('[data-rag-turn]').forEach(function(b){var matches=list.filter(function(r){return r.correlation.turnGroupId===b.dataset.ragTurn;});if(!matches.length)return;
            var agent=matches.filter(function(r){return r.correlation.agentId===b.dataset.ragAgent;});var use=agent.length?agent:matches;
            b.textContent=use.length===1?summary(use[0])+' · Inspect':'RAG · '+use.length+' context steps · Inspect';b.dataset.warning=String(use.some(isWarning));
        });
    }
    async function badges() { var current=session;if(!current)return;try{var list=await read(endpoint());if(current===session)updateBadges(list);}catch(_){} }
    async function loadSetup() {
        if(!session)return;var token=++setupRevision,current=session;setupError=null;
        try{var value=await read(endpoint('status'));if(token!==setupRevision||current!==session)return;setup=value;}
        catch(e){if(token!==setupRevision||current!==session)return;setup=null;setupError=e.message;}
        if(!detail)render();
    }
    async function selectRecord(id) {
        selected=id;var token=++detailRevision;var current=session;
        if(!id){detail=null;render();return;}
        try { var record=await read(endpoint(id));if(token!==detailRevision||current!==session)return;detail=record;render(); }
        catch(e){if(token===detailRevision){detail=null;render();status.textContent=e.message;}}
    }
    function schedule() {clearTimeout(timer);if(!drawer.hidden && (live||(detail&&!detail.completedAt&&Date.now()-new Date(detail.startedAt).getTime()<600000)))timer=setTimeout(refresh,1200);}
    async function refresh() {
        if(!session)return;var token=++revision;
        try {
            var list=await read(endpoint()+(turn?'?turnGroupId='+encodeURIComponent(turn):''));if(token!==revision)return;
            records=list;updateBadges(list);var old=picker.value;picker.replaceChildren();
            list.forEach(function(r){var option=new Option((r.correlation.agentId||'Agent')+(r.correlation.nodeId?' / '+r.correlation.nodeId:'')+' · '+(labels[r.outcome]||r.outcome)+' · '+new Date(r.startedAt).toLocaleString(),r.id);picker.append(option);});
            selected=list.some(function(r){return r.id===selected;})?selected:(list[0]&&list[0].id);picker.value=selected||'';picker.disabled=!list.length;
            status.textContent=list.length?'Showing '+list.length+' recorded step(s)'+(turn?' for this message.':'. Latest 200 steps; use a message badge for older requests.'):(live?'Waiting for context preparation…':'No retrieval records. Older messages cannot be reconstructed.');
            // Do not disrupt text selection or expanded result cards on completed records during polling.
            if(!detail||detail.id!==selected||!detail.completedAt)await selectRecord(selected);
        }catch(e){if(token===revision)status.textContent=e.message;}
        finally{if(token===revision)schedule();}
    }
    async function open(group,id) {
        if(!session)return;returnFocus=document.activeElement;turn=group||(live?liveTurn:null);selected=id||null;detail=null;drawer.hidden=false;document.body.classList.add('rag-inspector-open');tab='overview';render();drawer.querySelector('[data-rag-close]').focus();await Promise.all([loadSetup(),refresh()]);
    }
    function close(){drawer.hidden=true;document.body.classList.remove('rag-inspector-open');clearTimeout(timer);++revision;++detailRevision;if(returnFocus&&returnFocus.isConnected)returnFocus.focus();}
    function setSession(id){if(session===id)return;close();session=id;turn=null;selected=null;detail=null;records=[];setup=null;setupError=null;live=false;liveTurn=null;badges();loadSetup();}
    picker.addEventListener('change',function(){selectRecord(picker.value);});
    drawer.querySelector('[data-rag-close]').onclick=close;
    drawer.querySelector('[data-rag-refresh]').onclick=function(){detail=null;Promise.all([loadSetup(),refresh()]);};
    drawer.querySelector('[data-rag-all]').onclick=function(){turn=null;selected=null;detail=null;refresh();};
    drawer.querySelector('[data-rag-copy]').onclick=async function(){if(detail){try{await navigator.clipboard.writeText(JSON.stringify(detail,null,2));status.textContent='Diagnostic JSON copied.';}catch(_){status.textContent='Copy unavailable; use Download JSON.';}}};
    drawer.querySelector('[data-rag-export]').onclick=function(){if(!detail)return;var url=URL.createObjectURL(new Blob([JSON.stringify(detail,null,2)],{type:'application/json'}));var a=document.createElement('a');a.href=url;a.download='rag-'+detail.id+'.json';a.click();setTimeout(function(){URL.revokeObjectURL(url);},1000);};
    tabs.forEach(function(b,index){b.onclick=function(){tab=b.dataset.ragTab;render();};b.onkeydown=function(e){var next;if(e.key==='ArrowRight')next=(index+1)%tabs.length;else if(e.key==='ArrowLeft')next=(index+tabs.length-1)%tabs.length;else return;e.preventDefault();tabs[next].click();tabs[next].focus();};});
    drawer.addEventListener('keydown',function(e){if(e.key==='Escape'){e.preventDefault();close();}});
    document.addEventListener('click',function(e){var button=e.target.closest('[data-rag-turn], [data-rag-open-record], [data-rag-open]');if(!button)return;
        if(button.dataset.ragSession && button.dataset.ragSession!==session)return;
        open(button.dataset.ragTurn,button.dataset.ragOpenRecord);});
    global.AgctorRagInspector={setSession:setSession,refreshBadges:badges,open:open,
        begin:function(group){live=true;liveTurn=group;if(!drawer.hidden){turn=group;selected=null;detail=null;refresh();}},
        end:function(){live=false;liveTurn=null;badges();if(!drawer.hidden){detail=null;refresh();}}
    };
})(window);
