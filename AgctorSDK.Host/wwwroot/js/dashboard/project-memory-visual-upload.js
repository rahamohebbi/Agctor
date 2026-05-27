/**

 * PRD-023b: playground visual upload (init → PUT raw → complete).

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

        }



        function syncPlaceholder() {

            if (typeof options.onPlaceholderHint !== 'function' || !options.inputEl) return;

            var has = pending.some(function (p) {

                return p.state === 'uploaded';

            });

            options.onPlaceholderHint(has);

        }



        function closeTagPopover() {

            openTagIdx = null;

            renderChips();

        }



        function renderTagPopover(idx, entry) {

            var entities = typeof options.getEntities === 'function' ? options.getEntities() : [];

            var opts = '<option value="">(infer from message)</option>';

            entities.forEach(function (e) {

                var key = e.key || e.entityKey || e.id || '';

                if (!key) return;

                var label = e.label || e.displayName || key;

                var sel = entry.entityKey === key ? ' selected' : '';

                opts += '<option value="' + esc(key) + '"' + sel + '>' + esc(label) + '</option>';

            });

            return (

                '<div class="mt-2 w-full rounded border border-gray-200 bg-gray-50 p-2 dark:border-gray-600 dark:bg-gray-900/50" data-pm-tag-panel="' +

                idx +

                '">' +

                '<label class="block text-[10px] font-medium text-gray-600 dark:text-gray-300 mb-1">Tag person</label>' +

                '<select class="w-full rounded border border-gray-300 bg-white p-1 text-xs dark:bg-gray-700 dark:border-gray-600 dark:text-white" data-pm-tag-select>' +

                opts +

                '</select>' +

                '<div class="mt-1.5 flex gap-2">' +

                '<button type="button" class="text-[10px] font-medium text-blue-700 hover:underline dark:text-blue-300" data-pm-tag-save>Save</button>' +

                '<button type="button" class="text-[10px] text-gray-500 hover:underline" data-pm-tag-cancel>Cancel</button>' +

                '</div></div>'

            );

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

                var stateLabel =

                    st === 'uploaded'

                        ? p.entityKey

                            ? 'Ready · ' + esc(p.entityKey)

                            : 'Ready'

                        : st === 'error'

                          ? 'Failed'

                          : 'Uploading…';

                var tagBtn =

                    st === 'uploaded'

                        ? '<button type="button" class="text-blue-600 hover:underline dark:text-blue-300" data-pm-attach-tag>Tag</button>'

                        : '';

                html +=

                    '<div class="inline-flex flex-col max-w-[220px] rounded-lg border border-gray-200 bg-white px-2 py-1 text-xs dark:border-gray-600 dark:bg-gray-800" data-pm-attach-idx="' +

                    idx +

                    '">' +

                    '<div class="inline-flex items-center gap-2">' +

                    thumb +

                    '<span class="text-gray-600 dark:text-gray-300 max-w-[6rem] truncate">' +

                    esc(p.fileName || p.assetId || 'photo') +

                    '</span>' +

                    '<span class="text-gray-400 shrink-0" data-pm-attach-state>' +

                    esc(stateLabel) +

                    '</span>' +

                    tagBtn +

                    '<button type="button" class="text-gray-400 hover:text-red-600 shrink-0" data-pm-attach-remove title="Remove">×</button>' +

                    '</div>' +

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

        }



        function bindTagPanel(idx) {

            var panel = chipsEl.querySelector('[data-pm-tag-panel="' + idx + '"]');

            if (!panel) return;

            var saveBtn = panel.querySelector('[data-pm-tag-save]');

            var cancelBtn = panel.querySelector('[data-pm-tag-cancel]');

            var sel = panel.querySelector('[data-pm-tag-select]');

            if (saveBtn) {

                saveBtn.addEventListener('click', function () {

                    var entry = pending[idx];

                    if (entry) {

                        entry.entityKey = sel && sel.value ? sel.value : null;

                    }

                    closeTagPopover();

                    notify();

                });

            }

            if (cancelBtn) {

                cancelBtn.addEventListener('click', closeTagPopover);

            }

        }



        function setItemState(assetId, state, detail) {

            pending.forEach(function (p) {

                if (p.assetId === assetId) {

                    p.state = state;

                    if (detail) p.detail = detail;

                }

            });

            renderChips();

        }



        function uploadOne(file) {

            var scenarioId = options.getScenarioId();

            var sessionId = options.getSessionId();

            if (!scenarioId) {

                if (options.onError) options.onError('Select a project with a scenario before attaching photos.');

                return Promise.reject(new Error('no scenario'));

            }

            if (!sessionId) {

                if (options.onError) options.onError('Select a session before attaching photos.');

                return Promise.reject(new Error('no session'));

            }



            var previewUrl = URL.createObjectURL(file);

            var entry = {

                fileName: file.name,

                mime: file.type || 'image/jpeg',

                previewUrl: previewUrl,

                state: 'uploading',

                assetId: null,

                entityKey: null

            };

            pending.push(entry);

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

                    .map(uploadOne)

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

                return pending.filter(function (p) {

                    return p.state === 'uploaded' && p.assetId;

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

            }

        };

    }



    global.PMVisualUpload = { init: init };

})(window);


