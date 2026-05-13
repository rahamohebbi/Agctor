/**
 * Workspace browser: tree from GET tree; preview from GET file; Git changes under project root.
 */
(function () {
    const rootInput = document.getElementById('pm-root-input');
    const rootSave = document.getElementById('pm-root-save');
    const rootReload = document.getElementById('pm-root-reload');
    const rootNote = document.getElementById('pm-root-note');
    const rootBadge = document.getElementById('pm-root-badge');
    const treeMount = document.getElementById('pm-tree-mount');
    const preview = document.getElementById('pm-file-preview');
    const gitMeta = document.getElementById('pm-git-meta');
    const gitList = document.getElementById('pm-git-list');
    const btnTreeRefresh = document.getElementById('pm-btn-tree-refresh');
    const btnGitRefresh = document.getElementById('pm-btn-git-refresh');
    if (!treeMount || !preview) return;

    let selectedPath = null;
    let currentTree = null;
    const collapsedStorageKey = 'agctor.projectMemory.workspace.collapsedFolders.v1';
    const collapsedFolders = loadCollapsedFolders();

    function esc(s) {
        const d = document.createElement('div');
        d.textContent = s ?? '';
        return d.innerHTML;
    }

    function loadCollapsedFolders() {
        try {
            const raw = window.localStorage.getItem(collapsedStorageKey);
            const values = JSON.parse(raw || '[]');
            return new Set(Array.isArray(values) ? values.map(normalizePath) : []);
        } catch {
            return new Set();
        }
    }

    function saveCollapsedFolders() {
        try {
            window.localStorage.setItem(collapsedStorageKey, JSON.stringify(Array.from(collapsedFolders).sort()));
        } catch {
            // Browsers can deny local storage; folder toggling should still work for this page load.
        }
    }

    function folderKey(relPath) {
        const rel = normalizePath(relPath);
        return rel || '__root__';
    }

    function isFolderExpanded(relPath) {
        return !collapsedFolders.has(folderKey(relPath));
    }

    function expandAncestors(relPath) {
        const parts = normalizePath(relPath).split('/').filter(Boolean);
        collapsedFolders.delete('__root__');
        for (let i = 1; i < parts.length; i++) {
            collapsedFolders.delete(parts.slice(0, i).join('/'));
        }
    }

    function renderNode(node, depth) {
        const pad = depth * 12;
        const name = esc(node.name || '');
        const rel = node.relativePath != null ? String(node.relativePath) : '';
        if (node.isDirectory) {
            let inner = '';
            const kids = node.children || [];
            const expanded = isFolderExpanded(rel);
            if (expanded) {
                for (let i = 0; i < kids.length; i++) {
                    inner += renderNode(kids[i], depth + 1);
                }
            }
            return (
                '<div style="padding-left:' +
                pad +
                'px" class="py-0.5">' +
                '<button type="button" class="text-left rounded px-1 hover:bg-gray-100 dark:hover:bg-gray-700 pm-folder" aria-expanded="' +
                String(expanded) +
                '" data-path="' +
                encodeURIComponent(normalizePath(rel)) +
                '"><span class="text-gray-500">' +
                (expanded ? '▾' : '▸') +
                '</span> <span class="text-gray-500">📁</span> ' +
                name +
                '</button></div>' +
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

    function renderTree() {
        if (!currentTree) return;
        treeMount.innerHTML = renderNode(currentTree, 0);
        attachTreeClickHandlers();
        updateSelection(selectedPath);
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
        if (gitList) {
            gitList.querySelectorAll('.pm-git-file').forEach(function (btn) {
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
    }

    function attachTreeClickHandlers() {
        treeMount.querySelectorAll('.pm-folder').forEach(function (btn) {
            btn.addEventListener('click', function () {
                const raw = btn.getAttribute('data-path') || '';
                let rel;
                try {
                    rel = normalizePath(decodeURIComponent(raw));
                } catch {
                    rel = normalizePath(raw);
                }

                const key = folderKey(rel);
                if (collapsedFolders.has(key)) {
                    collapsedFolders.delete(key);
                } else {
                    collapsedFolders.add(key);
                }
                saveCollapsedFolders();
                renderTree();
            });
        });
        treeMount.querySelectorAll('.pm-file').forEach(function (btn) {
            btn.addEventListener('click', function () {
                const raw = btn.getAttribute('data-path') || '';
                try {
                    const rel = decodeURIComponent(raw);
                    updateSelection(rel);
                    loadPreview(rel);
                    syncPathQueryToUrl(rel);
                } catch {
                    updateSelection(raw);
                    loadPreview(raw);
                    syncPathQueryToUrl(raw);
                }
            });
        });
    }

    function getQueryPath() {
        const p = new URLSearchParams(window.location.search).get('path');
        if (!p) return null;
        return normalizePath(p);
    }

    /** Keeps the address bar in sync when a file is selected (shareable deep link, matches ?path=). */
    function syncPathQueryToUrl(relPath) {
        try {
            const u = new URL(window.location.href);
            u.searchParams.set('path', normalizePath(relPath));
            window.history.replaceState({}, '', u.pathname + u.search);
        } catch {
            // History API can throw in rare embed contexts; browsing should still work.
        }
    }

    function loadTree() {
        treeMount.innerHTML = '<p class="text-gray-500 text-xs">Loading tree…</p>';
        fetch('/api/project-memory/tree?maxDepth=8')
            .then(function (r) {
                if (r.status === 400)
                    return r.json().then(function (b) {
                        throw new Error(b.error || 'Set project root');
                    });
                return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
            })
            .then(function (node) {
                currentTree = node;
                const qp = getQueryPath();
                if (qp) {
                    expandAncestors(qp);
                    saveCollapsedFolders();
                    renderTree();
                    updateSelection(qp);
                    loadPreview(qp);
                } else {
                    renderTree();
                    preview.textContent = 'Select a file in the tree or under Git changes.';
                }
            })
            .catch(function (e) {
                treeMount.innerHTML = '<p class="text-amber-800 dark:text-amber-200">' + esc(e.message || '') + '</p>';
                preview.textContent = '';
            });
    }

    function setRootUiState(loaded, usingDefault) {
        if (!rootBadge) return;
        if (!loaded) {
            rootBadge.textContent = 'Not loaded';
            rootBadge.className = 'px-2 py-1 text-[11px] rounded border border-amber-300 text-amber-700 dark:border-amber-700 dark:text-amber-300';
            return;
        }

        if (usingDefault) {
            rootBadge.textContent = 'Default sample';
            rootBadge.className = 'px-2 py-1 text-[11px] rounded border border-blue-300 text-blue-700 dark:border-blue-700 dark:text-blue-300';
            return;
        }

        rootBadge.textContent = 'Custom';
        rootBadge.className = 'px-2 py-1 text-[11px] rounded border border-emerald-300 text-emerald-700 dark:border-emerald-700 dark:text-emerald-300';
    }

    function setRootNote(message, isError) {
        if (!rootNote) return;
        rootNote.textContent = message || '';
        rootNote.className = isError
            ? 'mt-2 text-xs text-red-700 dark:text-red-300'
            : 'mt-2 text-xs text-gray-600 dark:text-gray-400';
    }

    function loadRootStatus() {
        fetch('/api/project-memory/status')
            .then(function (r) {
                return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
            })
            .then(function (s) {
                if (rootInput) rootInput.value = s.projectRoot || '';
                setRootUiState(Boolean(s.projectLoaded), Boolean(s.usesDefaultSampleProjectRoot));
                if (s.error) {
                    setRootNote(s.error, true);
                } else if (s.projectRoot) {
                    setRootNote('Active root: ' + s.projectRoot, false);
                } else {
                    setRootNote('Set a project root to browse tree and Git changes.', false);
                }
            })
            .catch(function (e) {
                setRootUiState(false, false);
                setRootNote('Could not load root status: ' + (e.message || e), true);
            });
    }

    function saveRoot() {
        const value = (rootInput && rootInput.value ? rootInput.value : '').trim();
        if (!value) {
            setRootNote('Please enter an absolute folder path.', true);
            return;
        }

        if (rootSave) rootSave.disabled = true;
        setRootNote('Saving project root…', false);
        fetch('/api/project-memory/project-root', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ projectRoot: value })
        })
            .then(function (r) {
                if (!r.ok) {
                    return r.json().then(function (b) {
                        throw new Error((b && b.error) || String(r.status));
                    });
                }
                return r.json();
            })
            .then(function (res) {
                if (rootInput && res.projectRoot) rootInput.value = res.projectRoot;
                setRootNote((res.note || 'Saved.') + ' Reloading workspace view…', false);
                loadRootStatus();
                loadTree();
                loadGitChanges();
                preview.textContent = 'Select a file in the tree or under Git changes.';
            })
            .catch(function (e) {
                setRootNote('Save failed: ' + (e.message || e), true);
            })
            .finally(function () {
                if (rootSave) rootSave.disabled = false;
            });
    }

    function loadGitChanges() {
        if (!gitMeta || !gitList) return;
        gitMeta.textContent = 'Loading…';
        gitList.innerHTML = '';
        fetch('/api/project-memory/workspace/git-changes')
            .then(function (r) {
                if (r.status === 400)
                    return r.json().then(function (b) {
                        throw new Error(b.error || 'Set project root');
                    });
                return r.ok ? r.json() : Promise.reject(new Error(String(r.status)));
            })
            .then(function (d) {
                if (!d.gitAvailable) {
                    gitMeta.textContent = d.message || 'Git is not available for this folder.';
                    gitList.innerHTML =
                        '<p class="text-xs text-gray-600 dark:text-gray-400">' +
                        esc(d.message || '') +
                        '</p>';
                    return;
                }
                gitMeta.textContent =
                    (d.files && d.files.length
                        ? d.files.length + ' path(s) under project root — repo: '
                        : 'Clean — repo: ') + (d.gitRoot || '');
                if (!d.files || d.files.length === 0) {
                    gitList.innerHTML = '<p class="text-xs text-gray-600 dark:text-gray-400">No modified or untracked files under this project root.</p>';
                    return;
                }
                let h = '<ul class="space-y-1 text-xs font-mono">';
                for (let i = 0; i < d.files.length; i++) {
                    const f = d.files[i];
                    const rel = normalizePath(f.relativePath || '');
                    const st = esc(f.status || '');
                    h +=
                        '<li class="flex gap-2 items-baseline">' +
                        '<span class="shrink-0 text-gray-500 w-8">' +
                        st +
                        '</span>' +
                        '<button type="button" class="text-left text-blue-600 hover:underline dark:text-blue-400 pm-git-file break-all" data-path="' +
                        encodeURIComponent(rel) +
                        '">' +
                        esc(rel) +
                        '</button></li>';
                }
                h += '</ul>';
                gitList.innerHTML = h;
                gitList.querySelectorAll('.pm-git-file').forEach(function (btn) {
                    btn.addEventListener('click', function () {
                        const raw = btn.getAttribute('data-path') || '';
                        let rel;
                        try {
                            rel = normalizePath(decodeURIComponent(raw));
                        } catch {
                            rel = normalizePath(raw);
                        }
                        updateSelection(rel);
                        loadPreview(rel);
                        syncPathQueryToUrl(rel);
                    });
                });
            })
            .catch(function (e) {
                gitMeta.textContent = '';
                gitList.innerHTML = '<p class="text-red-600 text-xs">' + esc(e.message || '') + '</p>';
            });
    }

    if (btnTreeRefresh) btnTreeRefresh.addEventListener('click', loadTree);
    if (btnGitRefresh) btnGitRefresh.addEventListener('click', loadGitChanges);
    if (rootSave) rootSave.addEventListener('click', saveRoot);
    if (rootReload) rootReload.addEventListener('click', function () {
        loadRootStatus();
        loadTree();
        loadGitChanges();
    });
    if (rootInput) {
        rootInput.addEventListener('keydown', function (ev) {
            if (ev.key === 'Enter') saveRoot();
        });
    }

    loadRootStatus();
    loadTree();
    loadGitChanges();
})();
