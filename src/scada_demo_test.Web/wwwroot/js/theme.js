// Theme management for ALAM IOT SCADA
// Default theme is Light on initial visit / login.
// Users can toggle to Dark mode anytime, and their explicit choice is saved in localStorage.

window.theme = {
    initTheme: function () {
        var saved = localStorage.getItem('scada_theme');
        // Default to light. Only enable dark if the user explicitly saved 'dark'
        var isDark = (saved === 'dark');

        document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
        if (document.body) {
            document.body.classList.toggle('dark-theme', isDark);
        }
        return isDark;
    },

    setTheme: function (isDark) {
        var mode = isDark ? 'dark' : 'light';
        document.documentElement.setAttribute('data-theme', mode);
        localStorage.setItem('scada_theme', mode);
        if (document.body) {
            document.body.classList.toggle('dark-theme', isDark);
        }
    }
};

// Immediate execution in <head> to prevent theme flash:
(function () {
    try {
        var saved = localStorage.getItem('scada_theme');
        var isDark = (saved === 'dark');
        document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
    } catch (e) { }
})();
