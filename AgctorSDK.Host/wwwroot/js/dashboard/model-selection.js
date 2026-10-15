(function (global) {
    var catalog = null, pending = null;
    var labels = { generation: 'AI steps / answers', llmRouting: 'LLM routing', decisionRouting: 'Decision routing' };
    function load(refresh) {
        if (pending) return pending;
        if (catalog && !refresh) return Promise.resolve(catalog);
        pending = fetch('/api/model-settings/models').then(function (r) { return r.json().then(function (d) { if (!r.ok) throw new Error(d.error || 'Could not load models.'); return d; }); })
            .then(function (d) { catalog = d; return d; }).finally(function () { pending = null; });
        return pending;
    }
    function names(role) { return ((catalog && catalog.models) || []).filter(function (m) { return role === 'decisionRouting' ? m.decision : m.completion; }).map(function (m) { return m.name; }); }
    function populate(select, role, value, inherited) {
        if (!select) return Promise.resolve();
        var version = (select._modelVersion || 0) + 1;
        select._modelVersion = version;
        function draw() {
            var list = names(role), options = [new Option('Use default' + (inherited ? ' · ' + inherited : ''), '')];
            if (value && list.indexOf(value) < 0) options.push(new Option(value + (catalog ? ' (unavailable or incompatible)' : ''), value));
            list.forEach(function (name) { options.push(new Option(name, name)); });
            select.replaceChildren.apply(select, options);
            select.value = value || '';
        }
        draw();
        return load().then(function () { if (select._modelVersion === version) draw(); });
    }
    function fallback(defaults, role) { return defaults && defaults[role] || (role === 'decisionRouting' ? '' : catalog && catalog.applicationLlm || 'Application default'); }
    function describe(value, inherited) { return value ? value + ' · flow node' : inherited ? inherited + ' · inherited default' : 'No default configured — select a model here, in the scenario, or in chat.'; }
    function form(prefix, values) {
        Object.keys(labels).forEach(function (role) {
            populate(document.getElementById(prefix + role), role, values && values[role], role === 'decisionRouting' ? '' : 'Application').catch(function (e) {
                var status = document.getElementById(prefix + 'status'); if (status) status.textContent = e.message;
            });
        });
    }
    function readForm(prefix) { var values = {}; Object.keys(labels).forEach(function (role) { var e = document.getElementById(prefix + role); if (e && e.value) values[role] = e.value; }); return values; }
    global.AgctorModels = { load: load, populate: populate, fallback: fallback, describe: describe, form: form, readForm: readForm, labels: labels };
})(window);
