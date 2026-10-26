/** Mermaid enhancement for completed chat replies. Source remains the stored artifact. */
(function (global) {
    var loader, queue = Promise.resolve(), serial = 0;
    var header = /^(?:graph|flowchart)\s+(?:TD|TB|BT|LR|RL)\b|^(?:sequenceDiagram|erDiagram|stateDiagram(?:-v2)?)\b/;
    function isDiagram(source) { return header.test(source.trim()); }
    function markdown(source) {
        // Some models omit the code fence entirely. Only wrap a diagram at the
        // beginning of the reply; normal prose and other code languages stay intact.
        return isDiagram(source) ? '```mermaid\n' + source + '\n```' : source;
    }
    function library() {
        if (!loader) loader = new Promise(function (resolve, reject) {
            var script = document.createElement('script');
            script.src = '/lib/mermaid/mermaid.tiny-12.2.0.js';
            script.onload = function () { resolve(global.mermaid); };
            script.onerror = function () { script.remove(); loader = null; reject(new Error('Diagram renderer could not load. Refresh to retry.')); };
            document.head.appendChild(script);
        });
        return loader;
    }
    function button(label, action) {
        var b = document.createElement('button'); b.type = 'button'; b.textContent = label;
        b.addEventListener('click', action); return b;
    }
    function enhance(root) {
        if (!root) return;
        root.querySelectorAll('pre > code').forEach(function (code) {
            var source = code.textContent.trim();
            var language = Array.from(code.classList).find(function (c) { return c.indexOf('language-') === 0; });
            if (language !== 'language-mermaid' && (language || !isDiagram(source))) return;
            var pre = code.parentElement;
            if (pre.closest('.pm-diagram')) return;
            var card = document.createElement('section'); card.className = 'pm-diagram'; card.setAttribute('aria-label', 'Diagram');
            var toolbar = document.createElement('div'); toolbar.className = 'pm-diagram-toolbar';
            var title = document.createElement('strong'); title.textContent = 'Diagram'; toolbar.appendChild(title);
            var viewport = document.createElement('div'); viewport.className = 'pm-diagram-viewport';
            var visual = document.createElement('div'); visual.className = 'pm-diagram-visual'; viewport.appendChild(visual);
            var status = document.createElement('p'); status.className = 'pm-diagram-status'; status.setAttribute('role', 'status'); status.textContent = 'Rendering diagram…';
            var scale = 1, svgText = '', width = 0, height = 0;
            function zoom(next) {
                scale = Math.max(0.4, Math.min(3, next));
                var svg = visual.querySelector('svg');
                if (svg) { svg.style.width = Math.round(width * scale) + 'px'; svg.style.height = Math.round(height * scale) + 'px'; svg.style.maxWidth = 'none'; }
            }
            var toggle = button('Source', function () {
                pre.hidden = !pre.hidden; viewport.hidden = !pre.hidden;
                toggle.textContent = pre.hidden ? 'Source' : 'Diagram';
                toggle.setAttribute('aria-pressed', String(!pre.hidden));
            });
            toggle.setAttribute('aria-pressed', 'false');
            var out = button('−', function () { zoom(scale / 1.2); }); out.setAttribute('aria-label', 'Zoom out diagram');
            var plus = button('+', function () { zoom(scale * 1.2); }); plus.setAttribute('aria-label', 'Zoom in diagram');
            var fit = button('Fit', function () { zoom(Math.min(1, Math.max(100, viewport.clientWidth - 32) / width)); });
            var download = button('Download SVG', function () {
                if (!svgText) return;
                var url = URL.createObjectURL(new Blob([svgText], { type: 'image/svg+xml' }));
                var a = document.createElement('a'); a.href = url; a.download = 'diagram.svg'; a.click();
                setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
            });
            [out, plus, fit, download].forEach(function (b) { b.disabled = true; });
            toolbar.append(toggle, out, plus, fit, download);
            pre.replaceWith(card); card.append(toolbar, viewport, pre, status); pre.hidden = true;
            queue = queue.then(async function () {
                if (!card.isConnected) return;
                try {
                    if (source.length > 20000) throw new Error('Diagram is too large. Split it into smaller diagrams.');
                    if (!isDiagram(source)) throw new Error('Use a flowchart, sequence, ER, or state diagram.');
                    if (/%%\s*\{/.test(source)) throw new Error('Diagram configuration directives are not supported.');
                    var mermaid = await library();
                    if (!card.isConnected) return;
                    if (!global.DOMPurify) throw new Error('Diagram sanitizer could not load. Refresh to retry.');
                    mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', suppressErrorRendering: true,
                        maxTextSize: 20000, maxEdges: 200, theme: 'base', look: 'classic', htmlLabels: false, fontFamily: 'Arial, sans-serif',
                        flowchart: { useMaxWidth: false }, themeVariables: { primaryColor: '#eef2ff', primaryBorderColor: '#6366f1', primaryTextColor: '#172033', lineColor: '#64748b', fontSize: '14px' } });
                    await mermaid.parse(source);
                    var result = await mermaid.render('pm-diagram-' + (++serial), source);
                    if (!card.isConnected) return;
                    var feed = card.closest('.pm-chat-feed');
                    var followBottom = feed && feed.scrollHeight - feed.scrollTop - feed.clientHeight < 48;
                    visual.innerHTML = global.DOMPurify.sanitize(result.svg, { USE_PROFILES: { svg: true, svgFilters: true }, FORBID_TAGS: ['foreignObject', 'a', 'image'] });
                    var svg = visual.querySelector('svg');
                    if (!svg) throw new Error('No diagram was produced.');
                    svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
                    svg.setAttribute('role', 'img');
                    svg.setAttribute('aria-label', 'Diagram: ' + Array.from(svg.querySelectorAll('text')).map(function (text) { return text.textContent; }).filter(Boolean).join(', ').slice(0, 500));
                    var box = svg.viewBox.baseVal; width = box.width || 600; height = box.height || 400;
                    svgText = new XMLSerializer().serializeToString(svg);
                    zoom(Math.min(1, Math.max(100, viewport.clientWidth - 32) / width));
                    [out, plus, fit, download].forEach(function (b) { b.disabled = false; });
                    status.textContent = ''; status.hidden = true;
                    if (followBottom) feed.scrollTop = feed.scrollHeight;
                } catch (error) {
                    viewport.hidden = true; pre.hidden = false; toggle.disabled = true; toggle.textContent = 'Source';
                    status.textContent = 'Could not render this diagram. ' + String(error.message || error).slice(0, 350);
                    card.classList.add('pm-diagram-error');
                }
            });
        });
    }
    global.AgctorDiagrams = { enhance: enhance, markdown: markdown };
})(window);
