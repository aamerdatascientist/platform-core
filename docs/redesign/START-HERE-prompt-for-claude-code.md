Paste this into Claude Code from the `platform-core` folder after copying this folder to `docs/redesign/`:

---

Read `docs/redesign/HANDOFF.md` completely, then look at every image in `docs/redesign/screens/png/`. This is an approved redesign of the whole web app (platform-web) and it must be built exactly as shown.

Rules:
- Everything in `docs/redesign/screens/html/` is design reference only — never paste it into the app. Rebuild each screen in our existing React components, wired to real data, i18n and RTL. Use the inline styles in those HTML files for exact sizes and colours.
- Copy as-is: the values in `docs/redesign/tokens.css` and the strings in `docs/redesign/i18n-additions.json` (keep ar/en key parity at 100%).
- Follow section 11 (build order) one phase at a time. Start with phase 1: audit the code, map every section of the handoff to real files, list anything that conflicts, and send me that plan before writing code.
- Work on a branch. After each phase: build passes, `dotnet test` passes if the backend changed, check on staging at 390×844 and 1440×900 in Arabic/English × light/dark, then report. Nothing goes to production without my approval.
- Ask me before changing shared backend/frontend files or appsettings.
