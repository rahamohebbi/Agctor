/**
 * Shared persona tool catalog UI — uses GET /api/tools/for-persona/{id} as single source of truth.
 */
(function (global) {
    'use strict';

    var cache = Object.create(null);
    var inflight = Object.create(null);

    function esc(s) {
        var d = document.createElement('div');
        d.textContent = s == null ? '' : String(s);
        return d.innerHTML;
    }

    function normId(id) {
        return String(id || '').trim().toLowerCase();
    }

    function fetchForPersona(personaId, force) {
        var pid = String(personaId || '').trim();
        if (!pid) return Promise.resolve(null);
        var key = normId(pid);
        if (!force && cache[key]) return Promise.resolve(cache[key]);
        if (!force && inflight[key]) return inflight[key];

        inflight[key] = fetch('/api/tools/for-persona/' + encodeURIComponent(pid))
            .then(function (r) {
                if (!r.ok) {
                    return r.json().catch(function () { return null; }).then(function (b) {
                        var msg = (b && (b.message || b.errorMessage)) || ('Request failed: ' + r.status);
                        throw new Error(msg);
                    });
                }
                return r.json();
            })
            .then(function (dto) {
                cache[key] = dto;
                delete inflight[key];
                return dto;
            })
            .catch(function (e) {
                delete inflight[key];
                throw e;
            });

        return inflight[key];
    }

    function invalidatePersona(personaId) {
        delete cache[normId(personaId)];
    }

    function clearCache() {
        cache = Object.create(null);
        inflight = Object.create(null);
    }

    /** Short label for persona chips: allowed host tool ids. */
    function summarizeAllowedHostTools(dto) {
        if (!dto || !dto.hostTools || !dto.hostTools.length) return '';
        return dto.hostTools
            .filter(function (t) { return t.isAllowed; })
            .map(function (t) { return t.id; })
            .join(', ');
    }

    /** Full summary including semantic ops for chip title. */
    function summarizeToolsTitle(dto) {
        if (!dto) return '';
        var parts = [];
        if (dto.hostTools) {
            dto.hostTools.filter(function (t) { return t.isAllowed; }).forEach(function (t) {
                parts.push(t.id);
            });
        }
        if (dto.semanticTools) {
            dto.semanticTools.filter(function (t) { return t.isAllowed; }).forEach(function (t) {
                parts.push(t.id);
            });
        }
        if (dto.customAllowTokens && dto.customAllowTokens.length) {
            dto.customAllowTokens.forEach(function (t) { parts.push(t); });
        }
        return parts.join(', ');
    }

    function eligibleHostToolIds(dto) {
        if (!dto || !dto.hostTools) return [];
        return dto.hostTools.map(function (t) { return t.id; });
    }

    function renderYamlBaselinePills(container, dto) {
        if (!container) return;
        container.innerHTML = '';
        var allow = (dto && dto.yamlAllow) || [];
        if (!allow.length) {
            container.innerHTML =
                '<span class="text-[9px] text-gray-500 dark:text-gray-400">None in YAML — use Agent Studio to add <code class="text-[8px]">tools.allow</code>.</span>';
            return;
        }
        allow.forEach(function (tok) {
            var pill = document.createElement('span');
            pill.className =
                'inline-flex items-center rounded-full border border-gray-300 bg-gray-100 px-1.5 py-0.5 font-mono text-[8px] text-gray-700 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-200';
            pill.textContent = tok;
            pill.title = 'From agent YAML tools.allow';
            container.appendChild(pill);
        });
    }

    function appendCustomPills(wrap, tokens, dataAttr) {
        (tokens || []).forEach(function (tok) {
            var pill = document.createElement('span');
            pill.className =
                'inline-flex items-center gap-1 rounded-full border border-amber-300 bg-amber-50 px-2 py-0.5 font-mono text-[10px] text-amber-900 dark:border-amber-700 dark:bg-amber-900/30 dark:text-amber-100';
            pill.innerHTML = esc(tok) +
                ' <button type="button" class="font-bold leading-none hover:opacity-70" data-remove-' + dataAttr + '="' + esc(tok) + '" aria-label="Remove">×</button>';
            wrap.appendChild(pill);
        });
    }

    /**
     * Agent Studio allow editor: host + semantic toggles, custom allow pills, deny pills.
     * @param {HTMLElement} container
     * @param {object} dto from API
     */
    function renderAgentStudioTools(container, dto) {
        if (!container) return;
        container.innerHTML =
            '<div class="space-y-4 rounded-lg border border-gray-200 p-4 dark:border-gray-700">' +
            '<div><p class="text-xs font-semibold text-gray-800 dark:text-gray-200">Tools allow</p>' +
            '<p class="mt-0.5 text-[11px] text-gray-500 dark:text-gray-400">Toggle host and semantic ops; custom YAML tokens stay as pills.</p>' +
            '<div id="pm-tools-host-groups" class="mt-2 space-y-3"></div>' +
            '<div id="pm-tools-semantic" class="mt-2 flex flex-wrap gap-2"></div>' +
            '<div id="pm-tools-custom-allow" class="mt-2 flex flex-wrap gap-1"></div>' +
            '<div class="mt-2 flex gap-2"><input id="pm-tools-custom-add" type="text" placeholder="Custom allow token" class="flex-1 rounded border border-gray-300 px-2 py-1 text-xs font-mono dark:border-gray-600 dark:bg-gray-800 dark:text-white" />' +
            '<button type="button" id="pm-tools-custom-add-btn" class="rounded bg-gray-200 px-2 py-1 text-xs dark:bg-gray-700">Add</button></div></div>' +
            '<div><p class="text-xs font-semibold text-gray-800 dark:text-gray-200">Tools deny</p>' +
            '<div id="pm-tools-deny-pills" class="mt-2 flex flex-wrap gap-1"></div>' +
            '<div class="mt-2 flex gap-2"><input id="pm-tools-deny-add" type="text" placeholder="Deny token" class="flex-1 rounded border border-gray-300 px-2 py-1 text-xs font-mono dark:border-gray-600 dark:bg-gray-800 dark:text-white" />' +
            '<button type="button" id="pm-tools-deny-add-btn" class="rounded bg-gray-200 px-2 py-1 text-xs dark:bg-gray-700">Add</button></div></div>' +
            '</div>';

        var hostGroups = container.querySelector('#pm-tools-host-groups');
        var semanticEl = container.querySelector('#pm-tools-semantic');
        var customWrap = container.querySelector('#pm-tools-custom-allow');
        var denyWrap = container.querySelector('#pm-tools-deny-pills');

        var byGroup = {};
        (dto.hostTools || []).forEach(function (t) {
            if (!byGroup[t.group]) byGroup[t.group] = [];
            byGroup[t.group].push(t);
        });

        Object.keys(byGroup).sort().forEach(function (groupName) {
            var block = document.createElement('div');
            block.className = 'space-y-1';
            block.innerHTML = '<p class="text-[10px] font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400">' + esc(groupName) + '</p>';
            byGroup[groupName].forEach(function (t) {
                var lab = document.createElement('label');
                lab.className = 'flex cursor-pointer items-start gap-2 rounded px-1 py-0.5 hover:bg-gray-50 dark:hover:bg-gray-800/50';
                lab.innerHTML =
                    '<input type="checkbox" class="mt-0.5 shrink-0" data-pm-host-tool="' + esc(t.id) + '"' + (t.isAllowed ? ' checked' : '') + ' />' +
                    '<span class="min-w-0"><span class="block font-mono text-xs text-gray-900 dark:text-gray-100">' + esc(t.id) + '</span>' +
                    '<span class="block text-[11px] text-gray-600 dark:text-gray-400">' + esc(t.description || t.name || '') + '</span></span>';
                block.appendChild(lab);
            });
            hostGroups.appendChild(block);
        });

        (dto.semanticTools || []).forEach(function (t) {
            var lab = document.createElement('label');
            lab.className =
                'inline-flex cursor-pointer items-center gap-1.5 rounded-full border border-gray-300 px-2 py-1 text-xs dark:border-gray-600';
            lab.innerHTML =
                '<input type="checkbox" data-pm-semantic-tool="' + esc(t.id) + '"' + (t.isAllowed ? ' checked' : '') + ' />' +
                '<span class="font-mono">' + esc(t.id) + '</span>';
            lab.title = t.label || t.id;
            semanticEl.appendChild(lab);
        });

        appendCustomPills(customWrap, dto.customAllowTokens || [], 'custom-allow');
        appendCustomPills(denyWrap, dto.yamlDeny || [], 'deny');

        function bindRemove(wrap, attr, listRef) {
            wrap.querySelectorAll('[data-remove-' + attr + ']').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var tok = btn.getAttribute('data-remove-' + attr) || '';
                    var idx = listRef.indexOf(tok);
                    if (idx >= 0) listRef.splice(idx, 1);
                    btn.parentElement.remove();
                });
            });
        }

        var customTokens = (dto.customAllowTokens || []).slice();
        var denyTokens = (dto.yamlDeny || []).slice();
        bindRemove(customWrap, 'custom-allow', customTokens);
        bindRemove(denyWrap, 'deny', denyTokens);

        container.__pmCustomAllow = customTokens;
        container.__pmDenyTokens = denyTokens;

        var addCustomBtn = container.querySelector('#pm-tools-custom-add-btn');
        var addCustomInput = container.querySelector('#pm-tools-custom-add');
        if (addCustomBtn && addCustomInput) {
            addCustomBtn.addEventListener('click', function () {
                var v = addCustomInput.value.trim();
                if (!v || customTokens.indexOf(v) >= 0) return;
                customTokens.push(v);
                appendCustomPills(customWrap, [v], 'custom-allow');
                bindRemove(customWrap, 'custom-allow', customTokens);
                addCustomInput.value = '';
            });
        }

        var addDenyBtn = container.querySelector('#pm-tools-deny-add-btn');
        var addDenyInput = container.querySelector('#pm-tools-deny-add');
        if (addDenyBtn && addDenyInput) {
            addDenyBtn.addEventListener('click', function () {
                var v = addDenyInput.value.trim();
                if (!v || denyTokens.indexOf(v) >= 0) return;
                denyTokens.push(v);
                appendCustomPills(denyWrap, [v], 'deny');
                bindRemove(denyWrap, 'deny', denyTokens);
                addDenyInput.value = '';
            });
        }
    }

    function collectAllowFromEditor(container) {
        if (!container) return [];
        var allow = [];

        container.querySelectorAll('[data-pm-host-tool]').forEach(function (cb) {
            if (/** @type {HTMLInputElement} */ (cb).checked) {
                allow.push(cb.getAttribute('data-pm-host-tool') || '');
            }
        });

        container.querySelectorAll('[data-pm-semantic-tool]').forEach(function (cb) {
            if (/** @type {HTMLInputElement} */ (cb).checked) {
                allow.push(cb.getAttribute('data-pm-semantic-tool') || '');
            }
        });

        (container.__pmCustomAllow || []).forEach(function (t) {
            if (t && allow.indexOf(t) < 0) allow.push(t);
        });

        return allow.filter(Boolean);
    }

    function collectDenyFromEditor(container) {
        if (!container) return [];
        return (container.__pmDenyTokens || []).slice();
    }

    /**
     * Flow designer: grouped checkboxes for LlmNode step toolIds (eligible host tools only).
     */
    function renderFlowStepToolToggles(container, dto, selectedIds, escFn) {
        if (!container) return;
        container.innerHTML = '';
        var escLocal = escFn || esc;
        var eligible = eligibleHostToolIds(dto);
        if (!eligible.length) {
            container.innerHTML =
                '<p class="text-[9px] text-gray-500 dark:text-gray-400">No host tools are mapped for this persona on this step.</p>';
            return;
        }

        var selected = (selectedIds || []).map(function (x) { return String(x).toLowerCase(); });
        var byGroup = {};
        (dto.hostTools || []).forEach(function (t) {
            if (!byGroup[t.group]) byGroup[t.group] = [];
            byGroup[t.group].push(t);
        });

        Object.keys(byGroup).sort().forEach(function (groupName) {
            var groupEl = document.createElement('div');
            groupEl.className = 'space-y-1';
            var heading = document.createElement('p');
            heading.className = 'text-[8px] font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400';
            heading.textContent = groupName;
            groupEl.appendChild(heading);

            byGroup[groupName].forEach(function (t) {
                var lab = document.createElement('label');
                lab.className =
                    'flex cursor-pointer items-start gap-2 rounded border border-transparent px-1 py-0.5 hover:border-emerald-200/80 dark:hover:border-emerald-800/50';
                var cb = document.createElement('input');
                cb.type = 'checkbox';
                cb.className = 'mt-0.5 shrink-0';
                cb.setAttribute('data-flow-tool-id', t.id);
                cb.checked = selected.indexOf(String(t.id).toLowerCase()) >= 0;
                cb.title = 'Add to this flow step (merged with YAML at run time)';
                var text = document.createElement('span');
                text.className = 'min-w-0 flex-1';
                text.innerHTML =
                    '<span class="block font-mono text-[9px] text-gray-900 dark:text-gray-100">' +
                    escLocal(t.id) +
                    '</span>' +
                    '<span class="block text-[9px] leading-snug text-gray-600 dark:text-gray-400">' +
                    escLocal(t.description || t.name || t.id) +
                    '</span>';
                lab.appendChild(cb);
                lab.appendChild(text);
                groupEl.appendChild(lab);
            });

            container.appendChild(groupEl);
        });
    }

    global.AgctorPersonaToolsUi = {
        fetchForPersona: fetchForPersona,
        invalidatePersona: invalidatePersona,
        clearCache: clearCache,
        summarizeAllowedHostTools: summarizeAllowedHostTools,
        summarizeToolsTitle: summarizeToolsTitle,
        eligibleHostToolIds: eligibleHostToolIds,
        renderYamlBaselinePills: renderYamlBaselinePills,
        renderAgentStudioTools: renderAgentStudioTools,
        collectAllowFromEditor: collectAllowFromEditor,
        collectDenyFromEditor: collectDenyFromEditor,
        renderFlowStepToolToggles: renderFlowStepToolToggles
    };
})(typeof window !== 'undefined' ? window : this);
