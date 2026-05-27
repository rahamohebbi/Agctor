/**
 * PRD-023b: playground visual upload (init → PUT raw → complete) + optional Tag popover.
 * Exposes window.PMVisualUpload for project-memory-playground.js.
 */
(function (global) {
    function esc(s) {
        return String(s || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function defaultEntry(file) {
        return {
            file: file || null,
            fileName: file ? file.name : '',
            mime: file ? file.type || 'image/jpeg' : 'image/jpeg',
            previewUrl: null,
            state: 'uploading',
            assetId: null,
            entityKey: null,
            secondaryEntityKey: null,
            caption: '',
            sensitivity: 'normal',
            detail: null
        };
    }

    function init(options) {
        var pending = [];
        var chipsEl = options.chipsEl;
        var fileInput = options.fileInput;
        var attachBtn = options.attachBtn;
        var dropZone = options.dropZone;
        var openTagIdx = null;

        function notify() {
            if (typeof options.onPendingChange === 'function') options.onPendingChange(pending.slice());
            renderChips();
            syncPlaceholder();
            syncSendGate();
        }

        function syncPlaceholder() {
            if (typeof options.onPlaceholderHint !== 'function' || !options.inputEl) return;
            var has = pending.some(function (p) {
                return p.state === 'uploaded';
            });
            options.onPlaceholderHint(has);
        }

        function syncSendGate() {
            if (typeof options.onSendGate !== 'function') return;
            var uploading = pending.some(function (p) {
                return p.state === 'uploading';
            });
            var hasReady = pending.some(function (p) {
                return p.state === 'uploaded' && p.assetId;
            });
            options.onSendGate({ uploading: uploading, hasReady: hasReady, pending: pending.slice() });
        }

        function closeTagPopover() {
            openTagIdx = null;
            renderChips();
        }

        function entityOptions(selectedKey, includeInfer) {
            var entities = typeof options.getEntities === 'function' ? options.getEntities() : [];
            var opts = includeInfer !== false ? '<option value="">(infer from message)</option>' : '';
            entities.forEach(function (e) {
                var key = e.key || e.entityKey || e.id || '';
                if (!key) return;
                var label = e.label || e.displayName || key;
                var sel = selectedKey === key ? ' selected' : '';
                opts += '<option value="' + esc(key) + '"' + sel + '>' + esc(label) + '</option>';
            });
            return opts;
        }

        function renderTagPopover(idx, entry) {
            return (
                '<div class="mt-2 w-full rounded border border-gray-200 bg-gray-50 p-2 dark:border-gray-600 dark:bg-gray-900/50" data-pm-tag-panel="' +
                idx +
                '">' +
                '<label class="block text-[10px] font-medium text-gray-600 dark:text-gray-300 mb-1">Primary person</label>' +
                '<select class="w-full rounded border border-gray-300 bg-white p-1 text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" data-pm-tag-primary>' +
                entityOptions(entry.entityKey, true) +
                '</select>' +
                '<label class="block text-[10px] font-medium text-gray-600 dark:text-gray-300 mt-2 mb-1">Also in photo (optional)</label>' +
                '<select class="w-full rounded border border-gray-300 bg-white p-1 text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" data-pm-tag-secondary>' +
                entityOptions(entry.secondaryEntityKey, true) +
                '</select>' +
                '<label class="block text-[10px] font-medium text-gray-600 dark:text-gray-300 mt-2 mb-1">Caption (optional)</label>' +
                '<input type="text" maxlength="200" class="w-full rounded border border-gray-300 bg-white p-1 text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" data-pm-tag-caption value="' +
                esc(entry.caption || '') +
                '" placeholder="e.g. leg day week 2" />' +
                '<label class="block text-[10px] font-medium text-gray-600 dark:text-gray-300 mt-2 mb-1">Privacy</label>' +
                '<select class="w-full rounded border border-gray-300 bg-white p-1 text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" data-pm-tag-privacy>' +
                '<option value="normal"' +
                (entry.sensitivity === 'normal' ? ' selected' : '') +
                '>Normal</option>' +
                '<option value="sensitive"' +
                (entry.sensitivity === 'sensitive' ? ' selected' : '') +
                '>Sensitive</option>' +
                '<option value="do_not_infer"' +
                (entry.sensitivity === 'do_not_infer' ? ' selected' : '') +
                ">Don't analyze</option>" +
                '</select>' +
                '<div class="mt-1.5 flex gap-2">' +
                '<button type="button" class="text-[10px] font-medium text-blue-700 hover:underline dark:text-blue-300" data-pm-tag-save>Save</button>' +
                '<button type="button" class="text-[10px] text-gray-500 hover:underline" data-pm-tag-cancel>Cancel</button>' +
                '</div></div>'
            );
        }

        function chipStateLabel(p) {
            var st = p.state || 'uploading';
            if (st === 'uploaded') {
                if (p.sensitivity === 'do_not_infer') return 'Ready · analysis off';
                if (p.entityKey && p.secondaryEntityKey) {
                    return 'Ready · ' + esc(p.entityKey) + ' + ' + esc(p.secondaryEntityKey);
                }
                if (p.entityKey) return 'Ready · ' + esc(p.entityKey);
                return 'Ready';
            }
            if (st === 'error') return 'Failed';
            return 'Uploading…';
        }

        function renderChips() {
            if (!chipsEl) return;
            if (pending.length === 0) {
                chipsEl.innerHTML = '';
                chipsEl.classList.add('hidden');
                return;
            }
            chipsEl.classList.remove('hidden');
            var html = '';
            pending.forEach(function (p, idx) {
                var thumb = p.previewUrl
                    ? '<img src="' + esc(p.previewUrl) + '" alt="" class="h-10 w-10 rounded object-cover" />'
                    : '<span class="h-10 w-10 rounded bg-gray-200 dark:bg-gray-600 inline-block"></span>';
                var st = p.state || 'uploading';
                var tagBtn =
                    st === 'uploaded'
                        ? '<button type="button" class="text-blue-600 hover:underline dark:text-blue-300" data-pm-attach-tag>Tag</button>'
                        : '';
                var retryBtn =
                    st === 'error'
                        ? '<button type="button" class="text-amber-700 hover:underline dark:text-amber-300" data-pm-attach-retry>Retry</button>'
                        : '';
                var progress =
                    st === 'uploading'
                        ? '<div class="mt-1 h-0.5 w-full overflow-hidden rounded bg-gray-200 dark:bg-gray-600" aria-hidden="true"><div class="h-full w-1/3 animate-pulse bg-blue-500"></div></div>'
                        : '';
                html +=
                    '<div class="inline-flex flex-col max-w-[240px] rounded-lg border ' +
                    (st === 'error' ? 'border-red-300 dark:border-red-700' : 'border-gray-200 dark:border-gray-600') +
                    ' bg-white px-2 py-1 text-xs dark:bg-gray-800" data-pm-attach-idx="' +
                    idx +
                    '" ' +
                    (st === 'uploading' ? 'aria-busy="true"' : '') +
                    '>' +
                    '<div class="inline-flex items-center gap-2">' +
                    thumb +
                    '<span class="text-gray-600 dark:text-gray-300 max-w-[6rem] truncate">' +
                    esc(p.fileName || p.assetId || 'photo') +
                    '</span>' +
                    '<span class="text-gray-400 shrink-0" data-pm-attach-state>' +
                    chipStateLabel(p) +
                    '</span>' +
                    tagBtn +
                    retryBtn +
                    '<button type="button" class="text-gray-400 hover:text-red-600 shrink-0" data-pm-attach-remove title="Remove">×</button>' +
                    '</div>' +
                    progress +
                    (openTagIdx === idx ? renderTagPopover(idx, p) : '') +
                    '</div>';
            });
            chipsEl.innerHTML = html;

            chipsEl.querySelectorAll('[data-pm-attach-remove]').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var idx = parseInt(btn.closest('[data-pm-attach-idx]').getAttribute('data-pm-attach-idx'), 10);
                    var item = pending[idx];
                    if (item && item.previewUrl) URL.revokeObjectURL(item.previewUrl);
                    pending.splice(idx, 1);
                    closeTagPopover();
                    notify();
                });
            });

            chipsEl.querySelectorAll('[data-pm-attach-tag]').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var idx = parseInt(btn.closest('[data-pm-attach-idx]').getAttribute('data-pm-attach-idx'), 10);
                    openTagIdx = openTagIdx === idx ? null : idx;
                    renderChips();
                    bindTagPanel(idx);
                });
            });

            chipsEl.querySelectorAll('[data-pm-attach-retry]').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    var idx = parseInt(btn.closest('[data-pm-attach-idx]').getAttribute('data-pm-attach-idx'), 10);
                    var entry = pending[idx];
                    if (!entry || !entry.file) return;
                    entry.state = 'uploading';
                    entry.detail = null;
                    renderChips();
                    uploadOne(entry).catch(function () {
                        /* onError already surfaced */
                    });
                });
            });
        }

        function bindTagPanel(idx) {
            var panel = chipsEl.querySelector('[data-pm-tag-panel="' + idx + '"]');
            if (!panel) return;
            var saveBtn = panel.querySelector('[data-pm-tag-save]');
            var cancelBtn = panel.querySelector('[data-pm-tag-cancel]');
            var primarySel = panel.querySelector('[data-pm-tag-primary]');
            var secondarySel = panel.querySelector('[data-pm-tag-secondary]');
            var captionInput = panel.querySelector('[data-pm-tag-caption]');
            var privacySel = panel.querySelector('[data-pm-tag-privacy]');
            if (saveBtn) {
                saveBtn.addEventListener('click', function () {
                    var entry = pending[idx];
                    if (entry) {
                        entry.entityKey = primarySel && primarySel.value ? primarySel.value : null;
                        entry.secondaryEntityKey =
                            secondarySel && secondarySel.value ? secondarySel.value : null;
                        if (entry.entityKey && entry.secondaryEntityKey === entry.entityKey) {
                            entry.secondaryEntityKey = null;
                        }
                        entry.caption = captionInput ? String(captionInput.value || '').trim() : '';
                        entry.sensitivity = privacySel ? privacySel.value || 'normal' : 'normal';
                    }
                    closeTagPopover();
                    notify();
                });
            }
            if (cancelBtn) {
                cancelBtn.addEventListener('click', closeTagPopover);
            }
        }

        function applyFocusDefault(entry) {
            if (entry.entityKey) return;
            var focusKey =
                typeof options.getFocusEntityKey === 'function' ? options.getFocusEntityKey() : null;
            if (focusKey) entry.entityKey = focusKey;
        }

        function setItemState(assetId, state, detail) {
            pending.forEach(function (p) {
                if (p.assetId === assetId) {
                    p.state = state;
                    if (detail) p.detail = detail;
                }
            });
            renderChips();
            syncSendGate();
        }

        function uploadOne(entryOrFile) {
            var entry = entryOrFile && entryOrFile.file != null ? entryOrFile : null;
            var file = entry ? entry.file : entryOrFile;
            if (!entry) {
                entry = defaultEntry(file);
                pending.push(entry);
                notify();
            }

            var scenarioId = options.getScenarioId();
            var sessionId = options.getSessionId();
            if (!scenarioId) {
                if (options.onError) options.onError('Select a project with a scenario before attaching photos.');
                entry.state = 'error';
                notify();
                return Promise.reject(new Error('no scenario'));
            }
            if (!sessionId) {
                if (options.onError) options.onError('Select a session before attaching photos.');
                entry.state = 'error';
                notify();
                return Promise.reject(new Error('no session'));
            }

            if (!entry.previewUrl && file) entry.previewUrl = URL.createObjectURL(file);
            entry.state = 'uploading';
            entry.file = file;
            entry.fileName = file ? file.name : entry.fileName;
            entry.mime = file ? file.type || 'image/jpeg' : entry.mime;
            notify();

            var turnGroupId = options.getTurnGroupId ? options.getTurnGroupId() : null;
            return fetch('/api/visual/assets/init-upload', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    scenarioId: scenarioId,
                    contentType: entry.mime,
                    bytes: file.size,
                    sessionId: sessionId,
                    turnGroupId: turnGroupId
                })
            })
                .then(function (r) {
                    return r.json().then(function (b) {
                        if (!r.ok) throw new Error(b.message || b.error || String(r.status));
                        return b;
                    });
                })
                .then(function (init) {
                    entry.assetId = init.assetId;
                    var headers = init.uploadHeaders || {};
                    var h = { 'Content-Type': entry.mime };
                    Object.keys(headers).forEach(function (k) {
                        h[k] = headers[k];
                    });
                    return fetch(init.uploadUrl, { method: 'PUT', headers: h, body: file }).then(function (putRes) {
                        if (!putRes.ok) throw new Error('Upload failed (' + putRes.status + ')');
                        return init;
                    });
                })
                .then(function (init) {
                    return fetch('/api/visual/assets/' + encodeURIComponent(init.assetId) + '/complete', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ scenarioId: scenarioId })
                    }).then(function (r) {
                        return r.json().then(function (b) {
                            if (!r.ok) throw new Error(b.message || b.error || String(r.status));
                            return b;
                        });
                    });
                })
                .then(function () {
                    entry.state = 'uploaded';
                    applyFocusDefault(entry);
                    notify();
                    return entry;
                })
                .catch(function (e) {
                    entry.state = 'error';
                    notify();
                    if (options.onError) options.onError(e.message || String(e));
                    throw e;
                });
        }

        function addFiles(fileList) {
            var files = Array.prototype.slice.call(fileList || []);
            return Promise.all(
                files
                    .filter(function (f) {
                        return f && (f.type || '').indexOf('image/') === 0;
                    })
                    .map(function (f) {
                        return uploadOne(f);
                    })
            );
        }

        if (attachBtn && fileInput) {
            attachBtn.addEventListener('click', function () {
                fileInput.click();
            });
            fileInput.addEventListener('change', function () {
                addFiles(fileInput.files);
                fileInput.value = '';
            });
        }

        if (dropZone) {
            dropZone.addEventListener('dragover', function (e) {
                e.preventDefault();
                dropZone.classList.add('ring-2', 'ring-blue-400');
            });
            dropZone.addEventListener('dragleave', function () {
                dropZone.classList.remove('ring-2', 'ring-blue-400');
            });
            dropZone.addEventListener('drop', function (e) {
                e.preventDefault();
                dropZone.classList.remove('ring-2', 'ring-blue-400');
                addFiles(e.dataTransfer && e.dataTransfer.files);
            });
        }

        return {
            getPending: function () {
                return pending
                    .filter(function (p) {
                        return p.state === 'uploaded' && p.assetId;
                    })
                    .map(function (p) {
                        return {
                            assetId: p.assetId,
                            fileName: p.fileName,
                            mime: p.mime,
                            previewUrl: p.previewUrl || null,
                            entityKey: p.entityKey || null,
                            secondaryEntityKey: p.secondaryEntityKey || null,
                            caption: p.caption || null,
                            sensitivity: p.sensitivity || 'normal'
                        };
                    });
            },
            clear: function () {
                pending.forEach(function (p) {
                    if (p.previewUrl) URL.revokeObjectURL(p.previewUrl);
                });
                pending.length = 0;
                closeTagPopover();
                notify();
            },
            addFiles: addFiles,
            setItemState: setItemState,
            updatePreviewUrl: function (assetId, viewUrl) {
                pending.forEach(function (p) {
                    if (p.assetId === assetId && viewUrl) {
                        if (p.previewUrl) URL.revokeObjectURL(p.previewUrl);
                        p.previewUrl = viewUrl;
                    }
                });
                renderChips();
            },
            /** Build annotate POST body for sent-bubble Tag popover (PRD-023b). */
            buildAnnotateBody: function (tagData) {
                tagData = tagData || {};
                return {
                    entityKey: tagData.entityKey || null,
                    displayName: tagData.displayName || tagData.entityKey || null,
                    secondaryEntityKey: tagData.secondaryEntityKey || null,
                    secondaryDisplayName: tagData.secondaryDisplayName || tagData.secondaryEntityKey || null,
                    userCaption: tagData.caption || null,
                    sensitivity: tagData.sensitivity || 'normal'
                };
            },
            /** Shared Tag popover HTML for sent transcript attachments. */
            renderSentTagPopover: function (assetId, tagData) {
                tagData = tagData || {};
                return (
                    '<div class="mt-1 rounded border border-gray-200 bg-gray-50 p-2 text-[10px] dark:border-gray-600 dark:bg-gray-900/50" data-pm-sent-tag-panel="' +
                    esc(assetId) +
                    '">' +
                    '<label class="block font-medium text-gray-600 dark:text-gray-300 mb-1">Primary person</label>' +
                    '<select class="w-full rounded border border-gray-300 bg-white p-1 text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" data-pm-tag-primary>' +
                    entityOptions(tagData.entityKey, true) +
                    '</select>' +
                    '<label class="block font-medium text-gray-600 dark:text-gray-300 mt-2 mb-1">Also in photo</label>' +
                    '<select class="w-full rounded border border-gray-300 bg-white p-1 text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" data-pm-tag-secondary>' +
                    entityOptions(tagData.secondaryEntityKey, true) +
                    '</select>' +
                    '<label class="block font-medium text-gray-600 dark:text-gray-300 mt-2 mb-1">Caption</label>' +
                    '<input type="text" maxlength="200" class="w-full rounded border border-gray-300 bg-white p-1 text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" data-pm-tag-caption value="' +
                    esc(tagData.caption || '') +
                    '" />' +
                    '<label class="block font-medium text-gray-600 dark:text-gray-300 mt-2 mb-1">Privacy</label>' +
                    '<select class="w-full rounded border border-gray-300 bg-white p-1 text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" data-pm-tag-privacy>' +
                    '<option value="normal"' +
                    (tagData.sensitivity === 'normal' ? ' selected' : '') +
                    '>Normal</option>' +
                    '<option value="sensitive"' +
                    (tagData.sensitivity === 'sensitive' ? ' selected' : '') +
                    '>Sensitive</option>' +
                    '<option value="do_not_infer"' +
                    (tagData.sensitivity === 'do_not_infer' ? ' selected' : '') +
                    ">Don't analyze</option>" +
                    '</select>' +
                    '<div class="mt-1.5 flex gap-2">' +
                    '<button type="button" class="font-medium text-blue-700 hover:underline dark:text-blue-300" data-pm-sent-tag-save>Save</button>' +
                    '<button type="button" class="text-gray-500 hover:underline" data-pm-sent-tag-cancel>Cancel</button>' +
                    '</div></div>'
                );
            }
        };
    }

    global.PMVisualUpload = { init: init };
})(window);
