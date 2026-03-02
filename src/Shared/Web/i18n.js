// ═══════════════════════════════════════════════════════════════════════
// TAD.RV — Internationalization (i18n) Module
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Shared between Console and Teacher WebView2 UIs.
// Detects browser locale, loads the matching translation pack,
// and provides a t('key') function for runtime string lookup.
// ═══════════════════════════════════════════════════════════════════════

'use strict';

const TAD_I18N = (() => {
    const SUPPORTED = ['en', 'de', 'fr', 'nl', 'es', 'it', 'pl'];
    const LANG_LABELS = {
        en: 'English',
        de: 'Deutsch',
        fr: 'Français',
        nl: 'Nederlands',
        es: 'Español',
        it: 'Italiano',
        pl: 'Polski'
    };

    let currentLang = 'en';
    let strings = {};         // Current language pack
    let fallback = {};        // English fallback

    // ── Language detection ────────────────────────────────────────

    function detectLanguage() {
        // 1. Stored preference
        try {
            const stored = localStorage.getItem('tad_language');
            if (stored && SUPPORTED.includes(stored)) return stored;
        } catch { /* localStorage may not be available in WebView2 */ }

        // 2. Browser locale
        const nav = (navigator.language || navigator.userLanguage || 'en').substring(0, 2).toLowerCase();
        if (SUPPORTED.includes(nav)) return nav;

        // 3. Default
        return 'en';
    }

    // ── Initialisation ───────────────────────────────────────────

    function init(translationPacks) {
        fallback = translationPacks['en'] || {};
        currentLang = detectLanguage();
        strings = translationPacks[currentLang] || fallback;
        applyTranslations();
        return currentLang;
    }

    // ── Core t() function ────────────────────────────────────────

    function t(key, replacements) {
        let val = strings[key] || fallback[key] || key;
        if (replacements) {
            Object.keys(replacements).forEach(k => {
                val = val.replace(new RegExp(`\\{${k}\\}`, 'g'), replacements[k]);
            });
        }
        return val;
    }

    // ── Apply translations to DOM ────────────────────────────────

    function applyTranslations() {
        document.querySelectorAll('[data-i18n]').forEach(el => {
            const key = el.getAttribute('data-i18n');
            const translated = t(key);
            if (el.tagName === 'INPUT' && el.type !== 'checkbox' && el.type !== 'radio') {
                if (el.placeholder) el.placeholder = translated;
                else el.value = translated;
            } else {
                el.textContent = translated;
            }
        });

        // data-i18n-placeholder for input placeholders
        document.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
            el.placeholder = t(el.getAttribute('data-i18n-placeholder'));
        });

        // data-i18n-title for title attributes
        document.querySelectorAll('[data-i18n-title]').forEach(el => {
            el.title = t(el.getAttribute('data-i18n-title'));
        });

        // data-i18n-value for input default values
        document.querySelectorAll('[data-i18n-value]').forEach(el => {
            el.value = t(el.getAttribute('data-i18n-value'));
        });

        // Update HTML lang attribute
        document.documentElement.lang = currentLang;
    }

    // ── Language switching ────────────────────────────────────────

    function setLanguage(lang, translationPacks) {
        if (!SUPPORTED.includes(lang)) return;
        currentLang = lang;
        strings = translationPacks[lang] || fallback;
        try { localStorage.setItem('tad_language', lang); } catch { }
        applyTranslations();
    }

    function getLanguage() {
        return currentLang;
    }

    function getSupportedLanguages() {
        return SUPPORTED.map(code => ({ code, label: LANG_LABELS[code] }));
    }

    // ── Public API ───────────────────────────────────────────────

    return { init, t, setLanguage, getLanguage, getSupportedLanguages, applyTranslations };
})();

// Convenience alias
const t = TAD_I18N.t;
