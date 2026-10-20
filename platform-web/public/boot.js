// Runs before React mounts (loaded synchronously from index.html). Kept as a file rather
// than an inline <script> so the site's Content-Security-Policy can allow scripts from this
// site only, with no inline-script exception (see public/staticwebapp.config.json).

// Set dir/lang from the same localStorage key i18n/index.ts uses - avoids a flash of the
// wrong direction on load for a returning Arabic user.
(function () {
  var lang = 'en';
  try { lang = localStorage.getItem('lang') || 'en'; } catch (e) {}
  document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr';
  document.documentElement.lang = lang;
})();

// Set data-mode, same reasoning and same localStorage-key pattern (see src/theme/mode.ts).
// Defaults to dark - "Structural steel" is a dark-first industrial direction; light is the
// deliberate companion palette, not the default.
(function () {
  var mode = 'dark';
  try { mode = localStorage.getItem('mode') || 'dark'; } catch (e) {}
  document.documentElement.setAttribute('data-mode', mode);
})();
