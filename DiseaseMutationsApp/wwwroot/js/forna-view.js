// Draws the force-directed FORNA view in the browser with the vendored fornac library (Apache-2.0, wwwroot/lib/fornac),
// so it works on https deployments and sends the sequence nowhere. The library is loaded on first use.
(function () {
    let loading = null;

    function load() {
        if (window.fornac) return Promise.resolve();
        if (!loading) {
            loading = new Promise((resolve, reject) => {
                const script = document.createElement('script');
                script.src = 'lib/fornac/fornac.js';
                script.onload = () => (window.fornac ? resolve() : reject(new Error('fornac did not load')));
                script.onerror = () => reject(new Error('could not load lib/fornac/fornac.js'));
                document.head.appendChild(script);
            });
            loading.catch(() => { loading = null; });
        }
        return loading;
    }

    window.renderForna = async (elementId, structure, sequence) => {
        await load();
        const host = document.getElementById(elementId);
        if (!host) return false;
        host.innerHTML = '';
        const container = new window.fornac.FornaContainer('#' + elementId, {
            applyForce: true,
            allowPanningAndZooming: true,
            initialSize: [host.clientWidth || 600, 460],
            cssFileLocation: 'lib/fornac/fornac.css'
        });
        container.addRNA(structure, { structure: structure, sequence: sequence });
        return true;
    };
})();
