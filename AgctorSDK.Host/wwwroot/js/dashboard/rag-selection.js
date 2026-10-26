(function (global) {
    var catalog, pending;
    function load(refresh) {
        if (pending) return pending;
        if (catalog && !refresh) return Promise.resolve(catalog);
        pending = fetch('/api/model-settings/rag-providers').then(function (r) {
            if (!r.ok) throw new Error('Could not load RAG providers.');
            return r.json();
        }).then(function (data) { catalog = data; return data; }).finally(function () { pending = null; });
        return pending;
    }
    function label(id) { return id === 'None' ? 'None / markdown only' : id; }
    function populate(select, value, chat) {
        if (!select) return Promise.resolve();
        var version = (select._ragVersion || 0) + 1; select._ragVersion = version;
        function draw() {
            var ids = catalog ? catalog.providers : [];
            var options = [new Option(chat ? 'Use scenario / global default' : 'Use global default' + (catalog ? ' · ' + label(catalog.globalDefault) : ''), '')];
            if (value && ids.indexOf(value) < 0) options.push(new Option(label(value), value));
            ids.forEach(function (id) { options.push(new Option(label(id), id)); });
            select.replaceChildren.apply(select, options); select.value = value || '';
        }
        draw();
        return load().then(function () { if (select._ragVersion === version) draw(); });
    }
    global.AgctorRag = { load: load, populate: populate, label: label };
})(window);
