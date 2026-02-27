// Dashboard utilities (PRD-006). Pages may use inline scripts; this provides shared helpers if needed.
window.AgctorDashboard = window.AgctorDashboard || {
    escapeHtml: function(s) {
        if (s == null) return '';
        var d = document.createElement('div');
        d.textContent = s;
        return d.innerHTML;
    },
    apiBase: ''
};
