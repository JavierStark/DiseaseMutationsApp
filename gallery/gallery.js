// Theme only: the saved choice, else the OS preference. Everything else on the site works without JavaScript.
(function () {
    var root = document.documentElement;
    var saved = null;
    try { saved = localStorage.getItem('grna.theme'); } catch (e) { /* storage blocked */ }
    var dark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    root.setAttribute('data-bs-theme', saved === 'dark' || saved === 'light' ? saved : (dark ? 'dark' : 'light'));

    document.addEventListener('DOMContentLoaded', function () {
        var button = document.getElementById('theme-toggle');
        if (!button) return;
        function label() { button.textContent = root.getAttribute('data-bs-theme') === 'dark' ? 'Light theme' : 'Dark theme'; }
        button.hidden = false;
        label();
        button.addEventListener('click', function () {
            var next = root.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
            root.setAttribute('data-bs-theme', next);
            try { localStorage.setItem('grna.theme', next); } catch (e) { /* storage blocked */ }
            label();
        });
    });
})();
