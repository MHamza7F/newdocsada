// Theme management for ALAM IOT SCADA
// The reference palette follows the operating system until the user chooses a mode.
// Explicit choices are persisted in localStorage.

window.theme = {
    initTheme: function () {
        var saved = localStorage.getItem('scada_theme');
        var isDark = saved === 'dark' || (saved !== 'light' && window.matchMedia('(prefers-color-scheme: dark)').matches);

        document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
        if (document.body) {
            document.body.classList.toggle('dark-theme', isDark);
        }
        if (!window.theme._systemListener) {
            window.theme._systemListener = function (event) {
                // A saved choice always wins; an unset choice tracks the OS.
                if (localStorage.getItem('scada_theme') == null) {
                    window.theme.apply(event.matches);
                }
            };
            var media = window.matchMedia('(prefers-color-scheme: dark)');
            if (media.addEventListener) media.addEventListener('change', window.theme._systemListener);
            else if (media.addListener) media.addListener(window.theme._systemListener);
        }
        return isDark;
    },

    apply: function (isDark) {
        document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
        if (document.body) document.body.classList.toggle('dark-theme', isDark);
    },

    setTheme: function (isDark) {
        var mode = isDark ? 'dark' : 'light';
        localStorage.setItem('scada_theme', mode);
        window.theme.apply(isDark);
    }
};

// Immediate execution in <head> to prevent theme flash:
(function () {
    try {
        var saved = localStorage.getItem('scada_theme');
        var isDark = saved === 'dark' || (saved !== 'light' && window.matchMedia('(prefers-color-scheme: dark)').matches);
        document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
    } catch (e) { }
})();
