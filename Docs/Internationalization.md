# TAD.RV — Internationalization (i18n) Reference

**(C) 2026 TAD Europe — https://tad-it.eu**

> **Scope**: How the multi-language system works, how to add new languages,
> and how to add translatable strings to the UI.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Supported Languages](#2-supported-languages)
3. [How It Works](#3-how-it-works)
4. [Adding Translatable Text to HTML](#4-adding-translatable-text-to-html)
5. [Using Translations in JavaScript](#5-using-translations-in-javascript)
6. [Adding a New Language](#6-adding-a-new-language)
7. [Translation Key Conventions](#7-translation-key-conventions)
8. [File Locations](#8-file-locations)

---

## 1. Overview

TAD.RV uses a lightweight client-side i18n system called `TAD_I18N`. It operates entirely in the browser (WebView2) — no server round-trips, no build step. All translations are loaded as embedded JavaScript resources.

Both the **Management Console** and **Teacher Controller** share the same i18n module and language packs.

## 2. Supported Languages

| Code | Language | File |
|---|---|---|
| `en` | English | `Shared/Web/lang/en.js` |
| `de` | Deutsch | `Shared/Web/lang/de.js` |
| `fr` | Français | `Shared/Web/lang/fr.js` |
| `nl` | Nederlands | `Shared/Web/lang/nl.js` |
| `es` | Español | `Shared/Web/lang/es.js` |
| `it` | Italiano | `Shared/Web/lang/it.js` |
| `pl` | Polski | `Shared/Web/lang/pl.js` |

## 3. How It Works

### Architecture

```
TAD_I18N.register('en', { ... })     ← Language pack registers itself
TAD_I18N.register('de', { ... })     ← Multiple packs can be registered
TAD_I18N.setLocale('de')             ← Switch active locale
TAD_I18N.apply()                      ← Re-scan DOM for data-i18n attributes
```

### Loading Sequence (C# → WebView2)

1. C# reads language pack files from embedded resources
2. Each file is injected into the WebView2 via `ExecuteScriptAsync()`
3. `i18n.js` is loaded first (defines `TAD_I18N` module)
4. Language packs are loaded next (each calls `TAD_I18N.register()`)
5. App JavaScript calls `TAD_I18N.setLocale()` + `TAD_I18N.apply()`

### DOM Scanning

When `TAD_I18N.apply()` is called, it scans for these attributes:

| Attribute | Target | Example |
|---|---|---|
| `data-i18n` | `textContent` | `<span data-i18n="nav.dashboard">Dashboard</span>` |
| `data-i18n-placeholder` | `placeholder` | `<input data-i18n-placeholder="search.hint">` |
| `data-i18n-title` | `title` | `<button data-i18n-title="btn.save.tooltip">` |
| `data-i18n-value` | `value` | `<input data-i18n-value="btn.submit">` |

## 4. Adding Translatable Text to HTML

### Basic text

```html
<!-- Before -->
<h2>Dashboard</h2>

<!-- After -->
<h2 data-i18n="nav.dashboard">Dashboard</h2>
```

The English text remains as fallback content. When i18n loads, it replaces the text with the active locale's translation.

### Placeholders

```html
<input type="text"
       data-i18n-placeholder="alerts.search"
       placeholder="Search alerts...">
```

### Tooltips

```html
<button data-i18n-title="btn.freeze.tooltip"
        title="Freeze selected students">
    &#xE72E;
</button>
```

## 5. Using Translations in JavaScript

For dynamic text generated in JS code, use the `t()` helper:

```javascript
function t(key) {
    return (typeof TAD_I18N !== 'undefined')
        ? TAD_I18N.t(key)
        : key;
}

// Usage
showToast(t('deploy.success'), 'success');
document.title = t('nav.dashboard');
```

The `t()` function returns the translation for the current locale, or the key itself if no translation is found.

## 6. Adding a New Language

### 1. Create the language file

Copy `Shared/Web/lang/en.js` as a template:

```bash
cp Shared/Web/lang/en.js Shared/Web/lang/ja.js
```

### 2. Edit the registration call

Change the locale code from `'en'` to `'ja'`:

```javascript
// Shared/Web/lang/ja.js
TAD_I18N.register('ja', {
    'nav.dashboard': 'ダッシュボード',
    'nav.deploy':    'デプロイ',
    // ... translate all keys
});
```

### 3. Add to both .csproj files

In `Console/TadConsole.csproj` and `Teacher/TadTeacher.csproj`:

```xml
<EmbeddedResource Include="..\Shared\Web\lang\ja.js" Link="Web\lang\ja.js" />
```

### 4. Register in the C# loader

In both `Console/Views/MainWindow.xaml.cs` and `Teacher/MainWindow.xaml.cs`, add the new language to the resource loading loop.

### 5. Add to the language selector

The language selector in the HTML will automatically pick up any registered language, as long as the translation pack includes the `meta.flag` key for the flag emoji.

## 7. Translation Key Conventions

Keys follow a dot-notation hierarchy:

| Prefix | Scope | Example |
|---|---|---|
| `nav.*` | Sidebar / top navigation | `nav.dashboard`, `nav.deploy` |
| `dashboard.*` | Dashboard page | `dashboard.driver`, `dashboard.service` |
| `deploy.*` | Deployment page | `deploy.driverPath`, `deploy.success` |
| `policy.*` | Policy editor | `policy.save`, `policy.flag.BlockUsb` |
| `alerts.*` | Alerts page | `alerts.title`, `alerts.empty` |
| `classrooms.*` | Classrooms page | `classrooms.addRoom`, `classrooms.save` |
| `health.*` | Health checks | `health.title`, `health.allPassed` |
| `sysinfo.*` | System info section | `sysinfo.title`, `sysinfo.hostname` |
| `registry.*` | Registry section | `registry.title`, `registry.noData` |
| `teacher.*` | Teacher-specific | `teacher.freeze`, `teacher.unfreeze` |
| `common.*` | Shared / generic | `common.save`, `common.cancel` |
| `meta.*` | Language metadata | `meta.flag`, `meta.name` |

## 8. File Locations

| File | Purpose |
|---|---|
| `Shared/Web/i18n.js` | Core i18n module (`TAD_I18N`) |
| `Shared/Web/lang/en.js` | English translation pack (reference / master) |
| `Shared/Web/lang/*.js` | Other language packs |
| `Console/Views/MainWindow.xaml.cs` | Console i18n loader |
| `Teacher/MainWindow.xaml.cs` | Teacher i18n loader |
| `Console/Web/index.html` | Console HTML with `data-i18n` attributes |
| `Teacher/Web/dashboard.html` | Teacher HTML with `data-i18n` attributes |
| `Console/Web/app.js` | Console JS with `t()` calls |
| `Teacher/Web/dashboard.js` | Teacher JS with `t()` calls |

---

*See also: [Console-Guide.md](Console-Guide.md) · [Teacher-Guide.md](Teacher-Guide.md) · [Architecture.md](Architecture.md)*
