// Theme: persisted light/dark choice, falling back to the OS preference.
window.grnaTheme = {
    get() { try { return localStorage.getItem('grna.theme'); } catch { return null; } },
    apply(theme) { document.documentElement.setAttribute('data-bs-theme', theme); },
    current() { return document.documentElement.getAttribute('data-bs-theme') || 'light'; },
    toggle() {
        const next = this.current() === 'dark' ? 'light' : 'dark';
        this.apply(next);
        try { localStorage.setItem('grna.theme', next); } catch { /* storage blocked */ }
        return next;
    }
};

// Roving-tabindex keyboard navigation for the plate map (arrow keys, Home/End).
window.grnaPlateNav = {
    attach(container) {
        if (!container || container.dataset.navAttached) return;
        container.dataset.navAttached = '1';
        container.addEventListener('keydown', e => {
            const cell = e.target.closest('[data-r][data-c]');
            if (!cell) return;
            const r = +cell.dataset.r, c = +cell.dataset.c;
            let nr = r, nc = c;
            switch (e.key) {
                case 'ArrowRight': nc++; break;
                case 'ArrowLeft': nc--; break;
                case 'ArrowDown': nr++; break;
                case 'ArrowUp': nr--; break;
                case 'Home': nc = 0; break;
                case 'End': nc = 9999; break;
                default: return;
            }
            const cells = [...container.querySelectorAll('[data-r][data-c]')];
            let target = cells.find(x => +x.dataset.r === nr && +x.dataset.c === nc);
            if (!target && e.key === 'End') {
                const rowCells = cells.filter(x => +x.dataset.r === r);
                target = rowCells[rowCells.length - 1];
            }
            if (!target) return;
            e.preventDefault();
            cells.forEach(x => x.tabIndex = -1);
            target.tabIndex = 0;
            target.focus();
        });
    }
};

window.grnaCopy = async (text) => {
    try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
};

// WAI-ARIA tablist keyboard support: Left/Right/Home/End move focus and activate (automatic activation).
window.grnaTabs = {
    attach(list) {
        if (!list || list.dataset.tabsAttached) return;
        list.dataset.tabsAttached = "1";
        list.addEventListener("keydown", e => {
            const tabs = [...list.querySelectorAll('[role="tab"]')];
            const i = tabs.indexOf(document.activeElement);
            if (i < 0) return;
            let n = i;
            if (e.key === "ArrowRight") n = (i + 1) % tabs.length;
            else if (e.key === "ArrowLeft") n = (i - 1 + tabs.length) % tabs.length;
            else if (e.key === "Home") n = 0;
            else if (e.key === "End") n = tabs.length - 1;
            else return;
            e.preventDefault();
            tabs[n].focus();
            tabs[n].click();
        });
    }
};
