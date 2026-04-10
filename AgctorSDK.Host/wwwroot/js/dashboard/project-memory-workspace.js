/**
 * Workspace browser: JSON tree from GET tree; file preview from GET file?path=
 */
(function () {
    const treeMount = document.getElementById('pm-tree-mount');
    const preview = document.getElementById('pm-file-preview');
    if (!treeMount || !preview) return;
    let selectedPath = null;

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    function renderNode(node, depth) {
        const pad = depth * 12;
        const name = esc(node.name || '');
        const rel = node.relativePath != null ? String(node.relativePath) : '';
        if (node.isDirectory) {
            let inner = '';
            const kids = node.children || [];
            for (let i = 0; i < kids.length; i++) {
                inner += renderNode(kids[i], depth + 1);
            }
            return (
                '<div style="padding-left:' +
                pad +
                'px" class="py-0.5"><span class="text-gray-500">📁</span> ' +
                name +
                '</div>' +
                inner
            );
        }
        return (
            '<div style="padding-left:' +
            pad +
            'px" class="py-0.5">' +
            '<button type="button" class="text-left text-blue-600 hover:underline dark:text-blue-400 pm-file rounded px-1" data-path="' +
            encodeURIComponent(rel) +
            '">📄 ' +
            name +
            '</button></div>'
        );
    }

    function loadPreview(relPath) {
        preview.innerHTML = 'Loading…';
        const q = encodeURIComponent(relPath);
        fetch('/api/project-memory/file?path=' + q)
            .then(function (r) {
                if (!r.ok) return r.json().then(function (b) {
                    throw new Error(b.error || String(r.status));
                });
                return r.json();
            })
            .then(function (f) {
                let note = f.truncated ? '\n\n… truncated …' : '';
                preview.textContent = (f.content || '') + note;
            })
            .catch(function (e) {
                preview.textContent = 'Error: ' + (e.message || e);
            });
    }

    function normalizePath(relPath) {
        return String(relPath || '').replace(/\\/g, '/').replace(/^\/+/, '');
    }

    function updateSelection(path) {
        selectedPath = normalizePath(path);
        treeMount.querySelectorAll('.pm-file').forEach(function (btn) {
            const raw = btn.getAttribute('data-path') || '';
            let rel;
            try {
                rel = normalizePath(decodeURIComponent(raw));
            } catch {
                rel = normalizePath(raw);
            }
            const isActive = rel === selectedPath;
            btn.classList.toggle('bg-blue-100', isActive);
            btn.classList.toggle('dark:bg-blue-900/40', isActive);
            btn.classList.toggle('font-semibold', isActive);
        });
    }

    function getQueryPath() {
        const p = new URLSearchParams(window.location.search).get('path');
        if (!p) return null;
        return p.replace(/^\/+/, '');
    }

    fetch('/api/project-memory/tree?maxDepth=6')
        .then(function (r) {
            if (r.status === 400)
                return r.json().then(function (b) {
                    throw new Error(b.error || 'Set project root');
                });
            return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
        })
        .then(function (node) {
            treeMount.innerHTML = renderNode(node, 0);
            treeMount.querySelectorAll('.pm-file').forEach(function (btn) {
                btn.addEventListener('click', function () {
                    const raw = btn.getAttribute('data-path') || '';
                    try {
                        const rel = decodeURIComponent(raw);
                        updateSelection(rel);
                        loadPreview(rel);
                    } catch {
                        updateSelection(raw);
                        loadPreview(raw);
                    }
                });
            });
            const qp = getQueryPath();
            if (qp) {
                updateSelection(qp);
                loadPreview(qp);
            } else {
                preview.textContent = 'Select a file in the tree.';
            }
        })
        .catch(function (e) {
            treeMount.innerHTML = '<p class="text-amber-800 dark:text-amber-200">' + esc(e.message || '') + '</p>';
            preview.textContent = '';
        });
})();
