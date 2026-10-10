// Keep the mobile drawer's focus and scroll behavior local to its layout instance.
const instances = new WeakMap();

export function initialize(shell, dotnet) {
    dispose(shell);
    const sidebar = shell.querySelector('.sidebar');
    const main = shell.querySelector('.app-main');
    const media = window.matchMedia('(max-width: 768px)');
    let wasOpen = false;
    let previousOverflow;
    const dismiss = () => dotnet.invokeMethodAsync('DismissMobileSidebar').catch(() => {});
    const focusable = () => Array.from(sidebar.querySelectorAll('a[href], button:not([disabled])'))
        .filter(element => element.getClientRects().length > 0);
    const sync = () => {
        const open = media.matches && sidebar.classList.contains('mobile-open');
        sidebar.inert = media.matches && !open;
        main.inert = open;
        if (open && !wasOpen) {
            previousOverflow = document.body.style.overflow;
            document.body.style.overflow = 'hidden';
            focusable()[0]?.focus();
        } else if (!open && wasOpen) {
            document.body.style.overflow = previousOverflow;
            shell.querySelector(media.matches ? '.sidebar-mobile-toggle' : '.sidebar-desktop-toggle')?.focus();
        }
        wasOpen = open;
    };
    const onBreakpoint = () => { sync(); dismiss(); };
    const onKey = event => {
        if (!wasOpen) return;
        if (event.key === 'Escape') { event.preventDefault(); dismiss(); }
        if (event.key === 'Tab') {
            const elements = focusable();
            const first = elements[0];
            const last = elements[elements.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
        }
    };
    const observer = new MutationObserver(sync);
    observer.observe(sidebar, { attributes: true, attributeFilter: ['class'] });
    media.addEventListener('change', onBreakpoint);
    document.addEventListener('keydown', onKey);
    instances.set(shell, () => {
        observer.disconnect();
        media.removeEventListener('change', onBreakpoint);
        document.removeEventListener('keydown', onKey);
        if (wasOpen) document.body.style.overflow = previousOverflow;
        main.inert = false;
        sidebar.inert = false;
    });
    sync();
}

export function dispose(shell) {
    instances.get(shell)?.();
    instances.delete(shell);
}
