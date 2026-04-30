window.scrollToBottom = (el) => { if (el) el.scrollTop = el.scrollHeight; };

// Apply saved/system theme immediately — must run before first paint to avoid FOUC
(function () {
    const saved = localStorage.getItem('pf-theme');
    if (saved) document.documentElement.setAttribute('data-theme', saved);
})();

window.pfToggleTheme = function () {
    const root = document.documentElement;
    const current = root.getAttribute('data-theme') ||
        (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    const next = current === 'dark' ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    localStorage.setItem('pf-theme', next);
};
