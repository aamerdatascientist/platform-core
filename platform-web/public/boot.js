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
// The stored value is the person's preference: 'light', 'dark' or 'system' ("Match phone").
// Nothing stored = 'system' (redesign, 2026-11-06), resolved here from prefers-color-scheme so
// the first paint is already in the right mode - no flash.
(function () {
  var pref = 'system';
  try { pref = localStorage.getItem('mode') || 'system'; } catch (e) {}
  var mode = pref;
  if (pref !== 'light' && pref !== 'dark') {
    var dark = false;
    try { dark = window.matchMedia('(prefers-color-scheme: dark)').matches; } catch (e) {}
    mode = dark ? 'dark' : 'light';
  }
  document.documentElement.setAttribute('data-mode', mode);
})();
