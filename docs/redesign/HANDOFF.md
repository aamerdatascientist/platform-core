# Asas redesign — build handoff for Claude Code

Approved by Aamer on 2026-11-06 ("looks good — build it exactly as shown").
Design canvas (for humans): https://claude.ai/artifact/GDpsLin6hZF8Mi8u5yjfFC

This folder is everything needed to rebuild the Asas web app (`platform-web`, React + Vite + TypeScript, i18next, RTL-aware) to match the approved mockup, on phone and desktop, Arabic and English, light and dark.

---

## 0. Read this first — rules for this job

1. **Design only. Nothing in `screens/html/` is code to paste.** Those files are flat, static reference pages (inline styles, sample data, no logic). Rebuild every screen inside the existing React app, reusing its components, routes, API client, auth, i18n and RTL handling, wired to real data.
   - **Copy as-is:** the colour/size values in `tokens.css`, and the strings in `i18n-additions.json`.
   - **Everything else:** match it visually, implement it properly.
2. **Source of truth for looks:** `screens/png/` (rendered at 2× — a phone PNG is 780×1688 for a 390×844 screen; desktop PNGs are 2880 px wide for a 1440 px page). **Source of truth for exact numbers** (padding, radius, font size, colour): the inline styles in the matching `screens/html/*.html`.
3. **Sample data:** form fields, labels, users, roles and report settings are copied from staging. Project names ("مشروع تجريبي", "مشروع (مثال) ب…"), record rows, counts, the assignment grid and chart series are illustrative. Always render real data.
4. **Process (Aamer's standing way of working):** audit the real code first and map each section below to actual files before changing anything; plan; implement phase by phase (section 11) on a branch; verify on **staging** (`staging.asasksa.co`); the owner approves every production deploy. When suggesting a local test, give both commands in separate terminals: backend `dotnet run --project src\Platform.Api`, frontend `npm.cmd run dev` from `platform-web`.
5. **Keep what works:** server-side permissions, the single-flight token refresh in `api/client.ts`, the custom date picker logic, the RTL conventions in `CLAUDE.md` (logical CSS properties; deliberately-LTR rows pinned with `dir="ltr"`), the pre-mount `data-mode` script in `index.html`. Locale files must keep 100% key parity (ar/en).
6. **Ask before** touching a file both backend and frontend depend on, or anything in `appsettings*.json`.

---

## 1. What is in this folder

| Path | What it is |
|---|---|
| `HANDOFF.md` | This spec. Read fully before starting. |
| `tokens.css` | New colour, radius, size and type tokens for light and dark. Existing variable names kept, values changed, new ones added. All text pairs pass WCAG AA. |
| `i18n-additions.json` | Every new UI string, `en` and `ar`, grouped by namespace. Merge into `i18n/locales/en.json` and `ar.json`. |
| `screens/png/NN-*.png` | Approved screens, 2× renders. |
| `screens/html/NN-*.html` | The same screens as static HTML with exact inline styles. Links between phone screens work if opened in a browser. |

---

## 2. Screen map

Roles: **All** = every signed-in user. **PM** = Project manager (fills the daily progress report only). **Admin** = Administrator.

| # | Screen | Route | Role | Notes |
|---|---|---|---|---|
| 01 | Sign in | `/` (signed out) | — | Replaces the boxed card. |
| 02 | Today (home) | `/` and `/assignments` | All | New home. Replaces the empty "Pick a form" page and the old My assignments page. `/assignments` redirects to `/`. |
| 03 | Fill a form — step 1 | `/forms/:formId` (phone) | All | Stepped form, step 1. |
| 04 | Lookup bottom sheet | — (overlay on 03/05) | All | Every Lookup field and every long dropdown opens this. |
| 05 | Fill a form — step 2 with conditional fields | `/forms/:formId` (phone) | All | Shows chip groups, yes/no segments, a conditional block. |
| 06 | Final step — photos + review | `/forms/:formId` (phone) | All | Last step always ends with the review summary and the send button. |
| 07 | Sent | `/forms/:formId` after submit | All | Replaces the current success screen. |
| 08 | My entries | `/entries` (new) | All | The signed-in user's own submissions across forms. |
| 09 | Settings | `/settings` | All | Phone layout. The account/password screen stays at `/account`. |
| 10 | Today — English, dark | same as 02 | All | LTR + dark reference. |
| 11 | Form step 1 — English, dark, validation error | same as 03 | All | Error state reference. |
| 12 | Saved on this phone (offline) | `/forms/:formId` after submit with no connection | All | New offline queue. |
| 20 | Form page — Records tab + record drawer | `/forms/:formId?tab=records` (desktop) | Admin | Form page splits into **Fill** and **Records** tabs. |
| 21 | Form builder | `/builder/:formId` | Admin | Three panes: fields by step, field settings, live phone preview. |
| 22 | Users & roles | `/admin/users` | Admin | Role cards, table, user drawer. |
| 23 | Form assignments + daily tracking | `/admin/assignments` | Admin | Merges the old `/admin/assignments/status` page in (redirect it here). |
| 24 | Stakeholder reports | `/admin/reports` | Admin | |
| 25 | Executive overview (16:9) | `/dashboards/executive-overview` | Admin + share link | Restyle only; same data and calculations as today. |

The **builder home** (`/builder`, list of forms) was not drawn. Build it as a desktop card list in the same style as the Users table: one row per form with name, module, status pill and field count, and a primary "+ New form" button top-end that opens the existing create form in a drawer.

---

## 3. Approved product decisions (all five accepted "as shown")

1. **Admin pages move back into the desktop side menu** under an "Administration / الإدارة" group. This reverses the 2026-10-21 "sidebar = forms + Settings" decision. Settings still lists them for admins on a phone.
2. **Forms are filled in steps on a phone.** The builder gets a "step" concept (section 9.2). A form with no steps defined shows as one step.
3. **Every form name, field label, dropdown option label and step title has an English and an Arabic version** (section 9.1). The UI never mixes languages (no English "Yes/No/Select…" inside Arabic forms, or the reverse).
4. **"My entries" is available to every user, including PMs, showing only their own submissions** (section 9.3). PMs still cannot see other people's records, the dashboard, the builder or admin pages. This must be enforced on the server.
5. **Offline queue:** a submission made with no connection is kept on the phone and sent automatically later (section 9.6).

---

## 4. Visual system

- **Tokens:** replace the `:root` and `:root[data-mode="dark"]` blocks with `tokens.css`. Components must use variables, never raw hexes (the hexes in `screens/html` map 1:1 to the tokens; the mapping is in the comments of `tokens.css`).
- **Font:** Tajawal for everything, weights 400/500/700/**800** (add 800 to the Google Fonts URL). IBM Plex Mono only for field codes and form codes in the builder. Type sizes: classes at the bottom of `tokens.css`; use them as tokens, not necessarily as classes.
- **Never uppercase.** The current app uppercases headings ("MY ASSIGNMENTS", "SETTINGS"); remove all `text-transform: uppercase` and letter-spacing on headings.
- **Icons:** 24×24 viewBox, stroke 2 (2.2–2.6 for checkmarks), round caps and joins, `currentColor`, inline SVG (the paths are in `screens/html`). If `lucide-react` is already a dependency, its equivalents are fine. No emoji, no icon fonts. Directional icons (chevrons, arrows) flip in RTL: in Arabic, "forward" points left and the back arrow points right.
- **Logo mark:** three stacked rounded bars, skewed −18°, colours `--logo-1/2/3`, on a `--panel` tile with a 1px border (see 01 and the side menu).
- **Touch targets:** nothing tappable under 44×44. Phone inputs, steppers and lookup rows are 56px tall; chips 46; yes/no segments 52; the primary button 56.
- **Corners:** phone cards 20 (secondary cards 18), phone inputs 14, desktop cards 16, desktop inputs 10, chips and pills 999. See `--radius-*`.
- **Surfaces:** flat. Cards are `--panel` with a 1px `--border`. No shadows except `--elev-1` on the selected segment of a segmented control, and the bottom-sheet scrim. The old recessed/inset look is retired.
- **Motion (not visible in PNGs, required):** bottom sheet slides up 220ms ease-out over a fading scrim; conditional fields expand with height+opacity 180ms; step change slides 200ms in the reading direction (from inline-end in RTL); disable all of it under `prefers-reduced-motion`.
- **Focus:** visible focus on everything: `outline: 2px solid var(--accent); outline-offset: 2px`. Focused text inputs on a phone use a 1.5px `--accent` border plus `--focus-ring` (see the email field on 01).

---

## 5. App shells and breakpoints

| Width | Shell |
|---|---|
| < 1024px | **Phone shell:** content plus a bottom tab bar. No side menu, no hamburger. |
| ≥ 1024px | **Desktop shell:** 264px side menu on the inline-start side (right in Arabic, left in English) plus a fluid content area. |

### 5.1 Bottom tab bar (phone) — see 02, 08, 09, 10
- Height 80px + `env(safe-area-inset-bottom)`; `--panel` background, 1px top `--border`; four equal columns.
- Tabs: **Today** (home icon) → `/`; **Forms** (clipboard) → forms list; **My entries** (list) → `/entries`; **Settings** (sliders) → `/settings`.
- Item: icon 22px above a 13px label. Active item: label `--accent` weight 700, icon inside a 56×30 `--accent-soft` pill. Inactive: `--ink-soft`, weight 500.
- **Forms tab:** a phone list page (not drawn). Same style as "Other forms" on 02: group cards by module (module name as a 14px `--ink-soft` heading), one 56px row per form with a chevron. If the user can open only one form (PMs today), the Forms tab opens that form directly.
- Hidden while filling a form (03–07, 11, 12): those screens have their own top and bottom bars.

### 5.2 Side menu (desktop) — see 20–24 (left edge of each PNG in English, right edge in Arabic)
- 264px, `--sidebar` background, 1px `--border` on its inline-start edge, padding 18px 14px, items stacked with 18px between groups.
- Top: logo tile (34px) + "أساس / Asas", 20px weight 800.
- **Search / jump** button (42px, `--field-muted`, `Ctrl K` key hint pinned `dir="ltr"`). Opens a command palette dialog listing forms and pages the user can open, filtered as they type. `Ctrl+K` / `⌘K` opens it from anywhere; Esc closes it.
- Groups: first an ungrouped "Today · My tasks" item with an amber count badge (number of assignments still due today; hidden at 0); then one group per module (heading 12px weight 700 `--ink-soft`) listing its forms; then **Administration** (admins only): Executive overview ↗ (opens a new tab), Form builder, Form assignments, Stakeholder reports, Users & roles.
- Item: min-height 40, radius 10, 15px. Active: `--accent-soft` fill, `--accent-strong` text, weight 700.
- Bottom (pinned with `margin-top: auto`, divider above): avatar circle with the initial, name, role name in 12px, and a Settings icon button.
- Not sticky and not 100vh; it scrolls with the page. Below 1024px it does not render.

---

## 6. Components

Build these once and use them everywhere. Measurements are for the phone unless marked desktop.

| Component | Spec |
|---|---|
| **Primary button** | 56px phone / 42–46px desktop; `--accent` fill, `--accent-ink` text 18px/700 (desktop 15–16px/700); radius 16 (phone) / 12 (desktop). The send button on the final step uses `--success` fill and a send icon (06). |
| **Secondary button** | Same size; `--panel` fill, 1.5px `--border` (desktop 1px), `--ink` text weight 500. |
| **Danger button** | Secondary style, text `--danger`, border `--danger-border`. Destructive actions are never the primary filled button. |
| **Text link button** | No border, `--accent` text, ≥44px tap area. |
| **Field label** | 15px/700 above the control, 8px gap. Required marker: ` *` in `--danger` after the label. |
| **Hint** | 13px `--ink-soft` under the control. |
| **Inline error** | Control border 1.5px `--danger`, background `--danger-soft`; under it a 14px `--danger` line starting with an alert-circle icon (11). `aria-invalid` + `aria-describedby`. Errors appear on "Next"/"Send" and on blur after the first attempt, never while typing the first time. |
| **Chip group** (single select) | Wrapping row, 8px gaps. Chip: 46px tall, 18px horizontal padding (16 on 05), pill radius, 16px. Off: `--panel` + 1.5px `--border`, weight 500. On: `--accent-soft` + 1.5px `--accent`, text `--accent-strong` weight 700, leading check icon (03 weather). `role="radiogroup"` / `role="radio"`. |
| **Yes/No segments** | Two equal 52px buttons, 8px gap, radius 14, same on/off colours as chips. Order: Yes first (right in Arabic). Labels come from i18n (`نعم/لا`, `Yes/No`). |
| **Segmented control** | Track `--field-muted` (`#E9ECEF` on desktop), radius 12, padding 4. Segments 42px (desktop 34–38); selected = `--panel` fill + `--elev-1`, weight 700; others transparent `--ink-soft`. |
| **Number stepper** | [＋] [value] [−] in a row with 10px gaps; buttons 56×56 radius 14; value input fills the middle, centred, 24px/700, `inputmode="numeric"`. In RTL "+" sits on the right. Optional hint "Yesterday: {{value}}" from the user's previous entry of the same form. |
| **Date quick pick** | Three equal 48px buttons: Today / Yesterday / Other date (calendar icon). "Other date" opens the existing custom date picker. Under it a 13px line with the full chosen date. |
| **Lookup row** | 56px+, radius 14, `--panel`, 1.5px `--border`; leading icon in `--accent`; value 17px/500 with a 13px sub-line (code · status, or "Your last choice"); trailing chevron-down. Tap opens the bottom sheet. If it depends on another field that's empty, it is disabled and reads "Choose {{label}} first" in the current language. |
| **Bottom sheet** (04) | Scrim `--scrim`; sheet `--panel`, top radius 24, about 76% of viewport height; 40×5 handle; title 20px/800 + close button; search field 50px `--field-muted` radius 14; "Recent" section (last 3 choices of this field, stored per user in localStorage) then "All ({{n}})"; rows 64px with name 17px and a 13px meta line; the selected row has `--accent-soft` fill and a check; footer hint "Can't find it? Ask an administrator to add it." Esc, scrim tap and swipe-down close it; focus is trapped while open. **On desktop** the same content shows as a 480px-wide centred dialog. |
| **Conditional block** (05) | When a field appears because of another answer, it sits right under the controlling question, indented with `padding-inline-start: 12px` and a 3px `--accent` bar on the inline-start side. When the controlling field is a phase/type selector, the dependent fields sit in a card (`--panel`, radius 16, 14px padding) headed by a pill "Shown because you chose: {{value}}" (12px/700, `--accent-strong` on `--accent-soft`). |
| **Attachment tiles** (06) | 3-column grid, 100px tiles, radius 14. Filled tile shows the thumbnail and a size badge; the add tile is dashed 1.5px `--accent` with a camera icon and "Take a photo". `<input type="file" accept="image/jpeg,image/png,image/webp,image/heic,application/pdf" capture="environment">`. Hint lists the accepted types and the 20 MB limit. Tap a tile to preview or remove it. |
| **Step header** (03, 05, 06, 11) | `--panel` bar with a bottom border. Row: close (✕ on step 1, back arrow on later steps) 44×44 · title 17px/700 with the sub-line "Step n of N · Step title" 13px · "Draft saved" (cloud-check icon, 13px `--success`) at the end. Under it a progress bar: N equal segments, 5px tall, 6px gaps, done/current = `--accent`, upcoming = `--border`. |
| **Step footer** | `--panel` bar with a top border, padding 12px 20px 22px (+ safe area). Step 1: one full-width primary "Next: {{next step title}}" with a forward chevron. Later steps: a 96px secondary "Previous" plus the primary. Final step: full-width success "Send". Stays above the keyboard. |
| **Status pill** | 12–13px/700, pill radius, 3–5px × 9–12px padding. Colours: success (`--success` on `--success-soft`), warning (`--warning` on `--warning-soft`), danger (`--danger` on `--danger-soft`), info (`--accent-strong` on `--accent-soft`), neutral (`--ink-soft` on `--divider`). |
| **Day strip / grid cell** (02, 23) | Filled = `--success` fill; not filled = `--missed` fill; due today = transparent with 1.5px dashed `--warning`; Friday / outside the assignment = `--off`. Each cell has a `title` and an accessible name with the date and the state. Always show the legend. |
| **Card** | `--panel`, 1px `--border`, radius 20 (phone main) / 18 (phone secondary) / 16 (desktop). Rows inside are separated by 1px `--divider`. |
| **Table** (desktop) | Inside a card, wrapped in `overflow-x: auto`. Header row `--table-head`, 14px/700 `--ink-soft`, cells padding 12px 10px (14–18px at the row ends). Rows separated by `--divider`. Selected row: `--accent-row` fill plus a 3px `--accent` bar on the inline-start edge. Whole row clickable and keyboard-focusable; Enter opens it. Footer: "1–9 of 46" on the start side, Previous/Next buttons on the end side. |
| **Drawer** (desktop: 20, 22) | A card next to the table: `flex: 1 1 340px; max-width: 400px`. Header with title 18px/800, sub-line and close; body sections separated by `--divider`; footer actions pinned at the bottom. Below 1280px wide the drawer becomes an overlay sliding in from the inline-end side. |
| **Empty state** | Dashed 1.5px `--border` box, radius 12, centred 14px/700 title plus a 12–13px `--ink-soft` line. Always say what will fill it (25). |
| **Info banner** | `--accent-soft` fill, `--accent-strong` 14px text, radius 12, padding 10px 14px (21). The warning variant uses `--warning-soft` / `--warning-strong` (22, Telegram not connected). |

---

## 7. Generated forms — which control each field type gets

Forms come from the builder, so the renderer picks controls by field type. This table is the rule set; 03, 05, 06 and 11 are the reference.

| Field type | Phone control | Desktop control |
|---|---|---|
| Date/time | Date quick pick (Today / Yesterday / Other date) | Same |
| Number (whole) | Number stepper | Number input with `inputmode="numeric"` and steppers |
| Decimal | Text input, `inputmode="decimal"` | Same |
| Boolean | Yes/No segments | Same |
| Dropdown, ≤ 6 options and every label ≤ 24 characters | Chip group | Chip group |
| Dropdown, otherwise | Lookup row + bottom sheet with search | Lookup row + dialog |
| Lookup | Lookup row + bottom sheet (honour the existing cascade filter, e.g. zones of the chosen project) | Lookup row + dialog |
| Short text | Input, 56px | Input, 44px |
| Long text | Textarea, 3 rows, radius 14, no resize handle | Same |
| Attachment | Attachment tiles | Same |

- **Validation** uses the existing field rules (required, min, more-than, max, pattern, no future dates, unique) and the existing messages in `fieldRules.*`.
- **Conditional fields** use the existing visibility conditions and render as the conditional block (section 6).
- **Numbers:** accept Arabic-Indic digits and convert them (this already works server-side; keep it client-side too).
- **Dates and numbers** are formatted with `Intl` in the current UI language (Arabic: Arabic month names, Western digits as on the mockup).

---

## 8. Screens

Every screen exists in both languages and both modes. The PNG shows one combination; the others are the same layout mirrored (LTR/RTL) and/or recoloured by the tokens.

### 01 · Sign in
- Full-screen `--bg`, padding 20px 24px 32px. Top row: a pill language button showing the *other* language's name ("English" in Arabic, "العربية" in English) with a globe icon, and a 44px circular light/dark button (moon icon in light, sun icon in dark).
- Centre: logo tile 72px (radius 20), app name 30px/800, subtitle "Sign in to continue".
- Fields: Email (`type="email"`, `dir="ltr"` but aligned to the start of the reading direction), Password with a show/hide eye button at the inline-end.
- Hint under the fields: "Forgot your password? An administrator resets it for you from the Users page."
- Primary "Sign in" pinned at the bottom of the screen (thumb zone). Loading label "Signing in…". Errors as an inline error above the button using the existing `signIn.*` messages; 401 and 429 messages unchanged.
- Desktop: the same column, max-width 400px, vertically centred.

### 02 · Today (home) — all users
Data: existing my-assignments endpoint (today + last 2 weeks) and the forms list.
1. Header: date line (14px `--ink-soft`, e.g. "Saturday, 7 November") and greeting 24px/800: "Good morning / Good afternoon / Good evening, {{name}}" by local time (morning before 12:00, afternoon 12:00–17:00, evening after). Avatar circle with the initial on the inline-end side.
2. **Due today** section: title plus "{{left}} of {{total}} left". One card per assignment that isn't filled yet: icon tile (44px, `--accent-soft`), form name 18px/700, sub-line "{{module}} · {{n}} steps", a "Due today" warning pill, and a full-width 52px primary "Fill now" linking to the form. Filled assignments become compact rows: green check circle, form name, "Filled at {{time}}", and a "View" link to that entry.
3. **Last 2 weeks** card: 12 working-day cells (Saturday–Thursday) with day initials under them, "Filled {{filled}} of {{due}} days", and the legend. Only shown if the user has assignments.
4. **Other forms**: forms the user can open that are not assigned today, as 56px rows with chevrons.
5. If nothing is assigned today: the existing `assignments.mine.noneToday` message in a card, then Other forms.
6. When everything due is filled: a success note "You've done everything due today." above Other forms.

### 03, 05, 06 · Filling a form (phone)
- Steps come from the builder (section 9.2); each step is one screen. With no steps defined: a single step whose title is the form name.
- **Draft:** answers autosave to `localStorage` (key `draft:{userId}:{formId}`) 500ms after each change; the header shows "Draft saved". Opening a form with a draft resumes it at the step where the user left off. Attachments in a draft are kept in IndexedDB. The draft is cleared after a successful send. Closing (✕) keeps the draft and goes back.
- **Next** validates only the current step. Invalid → scroll to and focus the first invalid field.
- **Smart defaults (show them, but the user can change them):** the date defaults to Today; a Lookup with a remembered last choice pre-selects it and says "Your last choice"; the number stepper shows "Yesterday: n" when the user has a previous entry for the same form (from My entries).
- **03:** step 1 of the daily progress report: report date, project, workers present today, weather. (Fields appear in builder order. The PNG puts project before date; follow the real order.)
- **05:** phase chips; the phase-dependent unit field in the conditional card; "Was all the work planned for today done?" yes/no; delay yes/no with the cause revealed under it; problems yes/no with the description revealed.
- **06 (final step):** that step's own fields (here the photo), then a **review** made of one card per step: step title + "Edit" link (jumps back to that step), then label/value rows (label `--ink-soft` 15px on the start side, value 15px/700 on the end side). Only answered, visible fields are listed. Footer: success-coloured "Send" with a send icon.

### 04 · Lookup bottom sheet — see section 6.

### 07 · Sent
- Centred: success badge (96px soft circle with a 64px solid `--success` circle and a check), title "Submitted successfully", body "Your entry was saved at {{time}}." (existing strings).
- Summary card: Form · Project (if the form has one) · Attachments count.
- If the user has nothing else due today: the success note "You've done everything due today." in a `--success-soft` box with a home icon.
- Buttons at the bottom: primary "Back to Today", secondary "Submit another".
- Partial attachment failure keeps the existing message, shown as a warning box in place of the success note.

### 08 · My entries
- Title 26px/800. Filter chips scroll sideways: "All forms" (selected = solid `--ink` fill, white text) then one chip per form the user has entries for.
- Entries grouped by day (heading 14px/700 `--ink-soft`: "Today · Saturday 7 November", then plain dates). Card: form name 17px/700 + time at the end; a summary line built from the form's first 2–3 meaningful answers (project · phase · unit for the daily report; material · quantity · project for stock); tag pills for: delay = yes (warning), planned work done (success "Plan met" / danger "Plan not met"), attachments count (info).
- Tap opens a read-only detail page laid out like the review cards on 06, with photos. Editing stays admin-only, as today.
- Infinite scroll, 20 per page. Empty state: "You haven't sent anything yet."

### 09 · Settings (phone)
Sections in this order, each a heading (14px/700 `--ink-soft`) plus a card:
1. Profile card (no heading): avatar, display name, email (`dir="ltr"`), role pill.
2. **Display:** Language segmented control (العربية | English); Appearance segmented control (Light | Dark | Match phone). "Match phone" follows `prefers-color-scheme` live and is the new default for new users. Keep the pre-mount script working for all three values.
3. **Reminders:** Telegram row: icon tile, "Telegram", status line (green "Connected — reminders come to you" or the not-connected text), and the existing Connect / Connect again / Disconnect action as a secondary button.
4. **Account:** "Change password" row → `/account` (existing page restyled with the same components); "Sign out" row in `--danger`.
5. **Administration (admins only):** the existing admin links as 56px rows with their hint lines.
- **Do not ship** the grey paragraph at the bottom of 09 ("Administration pages … aren't shown here…"). It was a note for reviewers.
- **Desktop:** the same sections in one column, max-width 720px, inside the desktop shell.

### 10, 11 · English + dark
These are the same as 02 and 03, mirrored to LTR and in dark tokens. 11 also shows the validation error state.

### 12 · Saved on this phone (offline)
- A full-width banner at the top while offline: "No connection. You can keep working." (wifi-off icon, `--warning-strong` on `--warning-soft`).
- Centre: a clock badge in warning colours, title "Saved on this phone", body text from `offline.body`.
- "Waiting to send (n)" card listing queued entries (form, project, photo count, time).
- Buttons: primary "Back to Today", secondary "Try sending now".
- Sending works as described in section 9.6. When the queue empties, a toast says "Sent: n saved entries".

### 20 · Form page (desktop) — Fill / Records tabs
- Header: breadcrumb (module name), title 28px/800 with the status pill, and "Edit form" (admins) on the end side.
- Tabs (underline style, 3px `--accent` under the active one): **Fill** | **Records (count)**. The tab lives in the URL (`?tab=records`). PMs only get Fill and see no tab bar.
- **Fill tab (not drawn):** the same field controls as the phone, laid out as one column (max-width 720px) with one card per step (step title as the card heading) and a sticky footer holding the Send button. No step-by-step paging on desktop.
- **Records tab:** a filter row: search (searches all text answers), Project, Date range (default "Last 30 days", removable chip), Phase, and a "With a delay" toggle chip; a "Columns (shown of total)" menu on the end side that picks which fields are columns (default: the first 9 non-long-text fields; saved per user per form in localStorage).
- Table: date bold, then the chosen columns; booleans render as pills (planned work done) or a bold warning "yes" (delay); "Sent by" last.
- **Record drawer:** title = report date, sub-line "Sent by {{name}} at {{time}}"; every visible answer as label/value rows; attachments as 84×64 thumbnails plus a dashed "+ Add" tile (uses the existing attachments panel logic); footer: primary "Edit" (opens the Fill layout pre-filled, as today) and danger "Delete" (existing soft delete and confirmation). The workflow status section appears under the attachments only if the form has a workflow.

### 21 · Form builder
- Header: breadcrumb "Form builder › module", title, pill "Published · n fields", the form code in mono; buttons on the end side: **Access** (lock icon; the label summarises access, e.g. "Access: Project manager + 3 users"; opens the existing role/user access editor in a dialog) and "Open form ↗".
- Info banner (published forms only): the existing `formBuilder.liveEditNote` text.
- **Pane 1, fields** (`flex: 1 1 300px; max-width: 360px`): fields grouped under step headings ("Step 1 · Basics" + field count). Row: grip handle, label 14px/700, a type line 12px (type · key rule, e.g. "Number · 0 or more", "Lookup · Projects", "Dropdown · 5 options"), and a "Conditional" pill when it has a visibility condition. Drag to reorder within or between steps (keyboard alternative: keep the move up/down buttons in the row's menu). The selected row has an `--accent` border and `--accent-row` fill. At the bottom a dashed "+ Add field or step" button opens the existing add-field form (plus the new labels and step choice).
- **Pane 2, field settings** (flexible): stacked cards:
  1. **Basics:** title = label, a meta line "type · Step n"; Name in Arabic and Name in English inputs side by side (English is new, marked with a "New" pill until filled); code in a mono chip with "Rename code" and "Change type" buttons (existing confirmation flows unchanged).
  2. **When is it shown?** A sentence builder: "Shown only when [field ▾] = [value ▾]", or "Always show it". This replaces the "Only show when…" dialog; same stored condition.
  3. **Rules:** a Required switch ("only when shown" for conditional fields) and the type-specific rules (min/max/more-than/pattern/no future dates/unique/max characters) from the existing Rules dialog, inline.
  4. Actions: "Archive field (keeps the data)" and danger "Delete permanently" (existing flows and confirmations).
  - For Lookup fields add a **Filter** card (existing "Filter by…" setting) after "When is it shown?". For Dropdown fields add an **Options** card listing value + Arabic label + English label rows.
- **Pane 3, live preview** (`max-width: 300px`): a 280×560 phone frame (8px `--ink` border, radius 32) rendering the selected field's step with the real form renderer in preview mode; the selected field is outlined with a 2px dashed `--warning` frame. An عربي / English toggle under it switches only the preview's language.
- Below 1280px the preview pane drops under the other two.

### 22 · Users & roles
- Header: title; on the end side "+ New role" (secondary) and "+ New user" (primary). Each opens the existing create form in a dialog.
- Role cards row (wrap, `flex: 1 1 180px`): role name 15px/800, user count, description (or "System" for Administrator).
- Filter row: search (name or email) and a segmented control All (n) / Active (n) / Deactivated (n).
- Table: user (avatar initial + name + email `dir="ltr"`), role pill(s), reminders (e.g. "Telegram ✓ · WhatsApp" in `--success`, "No Telegram" in `--warning`, "—"), status pill.
- **User drawer:** header (avatar, name, email); **Roles** as toggle chips (saves immediately; the existing self-demotion guard applies); **Reminders:** WhatsApp number + "Test message", Telegram status box (warning style when not connected) with "Link to a group" (existing steps open in a small dialog); **Password:** "Reset password" (existing flow and result dialog); footer: danger "Deactivate account" / secondary "Reactivate".

### 23 · Form assignments + daily tracking
- Header: title, the one-line rule "One form every working day, Saturday to Thursday. Friday is off.", primary "+ Assign a form" (on narrow screens it scrolls to / opens the assign panel).
- **Today card:** "Today · Saturday 7 November", headline "{{filled}} of {{total}} filled", pills (Filled n, Due today n, Not filled n), and "Remind those who haven't filled (n)", which sends the existing Telegram reminder to everyone still pending today and then shows the existing "reminders sent / failed" result.
- **Grid card** "Who filled and when · last 2 weeks": filters (All forms ▾, date range ▾ defaulting to the last 14 days). One row per assignment (user + form name), one column per day (day initial + date number, Fridays included and greyed), and a last column "Rate" = filled / due. Cells per section 6. Clicking a cell opens that entry if one exists; clicking a day header shows the old by-day list for that date in a popover. Horizontal scroll inside the card.
- **Assign panel** (end-side column, `max-width: 380px`): Form select; Users as checkbox rows showing Telegram status; From (date quick pick) and To ("No end date" by default); Note; primary "Assign n users" with correct Arabic plurals. Existing validations and "already assigned, skipped" message.
- Ended or stopped assignments: a "Show ended" toggle on the grid lists them greyed with "Ended".

### 24 · Stakeholder reports
- Header: title, the one-line explanation with the send time from settings (e.g. "6:00 PM Saudi time"), and three setup pills on the end side (green "✓ Claude analysis / PDF service / WhatsApp"; danger pill with "✕" for any that isn't configured).
- A 4-column row: Daily / Weekly / Monthly schedule cards (name, rule + time, and "Next: {{date}}" computed client-side: next working day; next Thursday; last day of the month), then the spend card ("Claude spend this month", "$0.14 / $15.00" `dir="ltr"`, progress bar).
- Two cards side by side: **Recipients (n)** (row: avatar, name, number `dir="ltr"`, pills for the reports they get; Edit/Remove in a row menu; "+ Add a recipient") and **Make a report now** (segmented Daily/Weekly/Monthly, "Up to" date button, "With Claude's analysis" checkbox, "Preview without sending" and "Send now" with the existing confirmation).
- **Recent reports** table: made at, report · trigger, result (coloured dot + plain-language result), files (Phone PDF / A4 PDF buttons). **Raw technical errors are never shown in the table**; a "Show reason" button opens them in a dialog with a Copy button.

### 25 · Executive overview (16:9)
Restyle only. Keep every calculation, filter, the share link and the full-screen behaviour.
- 1600×900 design, scales to fit as today. Header: logo, title, "As of … · daily reports since …", Project and Stage filters, "Copy share link", full-screen icon button.
- 8 tiles in one row (equal width, min-height 108): label 12px, value 28px/800, detail 12px. Value colour: `--success` for good, `--danger` for late/behind, `--ink` otherwise. **No coloured side borders.**
- Row 2: Projects table (bar = work complete in `--accent` on `--border`, a 2px tick for time elapsed; five phase segments; schedule text in `--danger` when behind; last report in `--danger` with "No report for n days" when quiet) | **Plan met, by week as bars** (replaces the line chart; bar colour by value: ≥75% `--accent`, 40–74% a muted accent, <40% `--danger`).
- Row 3: Workers on site by week (bars) · Plan met by weather (horizontal bars; extreme heat in `--accent2`) · Why days were delayed (horizontal bars in `--danger`, Arabic causes shown `dir="rtl"` even in English) · Stock by material · Latest problems; each with the empty states shown.
- Same layout in Arabic (mirrored) and light mode.

---

## 9. Backend and data changes

Each item says why it is needed. Write EF migrations for schema changes, keep the dynamic-schema rules in `CLAUDE.md`, and add tests.

### 9.1 Bilingual labels (decision 3)
- Add an English label next to every Arabic one: form name, field label, dropdown option label, step title (for example `NameEn` / `LabelEn`, or a `{ar, en}` value object; follow the codebase's conventions). Existing labels are Arabic, so migrate them as the Arabic value and leave English empty.
- API returns both; the client picks by UI language, **falling back to the other language when one is empty** (never blank).
- Builder: both inputs (section 8, screen 21). Module names already have translations in `moduleName.*`.
- Fixed UI words inside forms (Yes/No, Select…, Choose X first) always come from i18n in the UI language.

### 9.2 Steps (decision 2)
- A form version gets an ordered list of steps (id, Arabic title, English title); each field gets an optional step id. Fields without a step go into one default step.
- Builder: create, rename, reorder and delete steps (deleting moves the step's fields to the previous step); drag fields between steps.
- Seed the daily progress report with: 1 Basics / الأساسيات (date, project, workers, weather) · 2 Progress / التقدم (phase … planned-work question) · 3 Photos / الصور (site photo). Do this on staging through the builder or a reviewed script in `scripts/`, not in a migration.
- Submission stays one request at the end; steps are presentation only.

### 9.3 My entries (decision 4)
- New endpoint, e.g. `GET /api/submissions/mine?formId=&cursor=`: the caller's own non-deleted submissions across forms they can access, newest first, with resolved Lookup/Dropdown labels in both languages and the attachment count. Enforce "own rows only" on the server for every role.
- Read-only detail for an own submission (reuse the existing detail endpoint with an owner-or-admin check).
- Add the PM role's access to it; nothing else about PM permissions changes.

### 9.4 Records list (screen 20)
- Server-side filters on the existing submissions list: date range, project, phase (or any dropdown field), has-delay (any boolean field), free-text search. Keep paging.
- Return resolved labels for Lookup fields (this also fixes the known raw-GUID bug in `SubmissionsTable`).

### 9.5 Assignments grid (screen 23)
- If missing: one endpoint returning, for a date range, each assignment row with a per-day status (filled with submission id / missed / pending / off), plus today's totals. Reuse the existing tracking logic.
- "Remind pending" action: reuse the existing reminder sender for today's pending users; return sent/failed counts.

### 9.6 Offline queue (decision 5) — client side, plus one server change
- Client: on send with no connection (or a network error), store the submission and its files in IndexedDB with a client-generated **idempotency key**; show screen 12. Retry when the `online` event fires, when the app opens, every 60s while queued, and on "Try sending now". Use Background Sync where supported.
- Server: accept an `Idempotency-Key` header on submission create and return the original result for a repeated key (so a retry can never create a duplicate record). Store keys for 7 days.
- A queued entry counts as filled for an assignment on the day it was *created* on the phone: send the created-at time with the submission and use it for the assignment day. Validate it server-side (not in the future; at most 3 days old).

### 9.7 Small things
- Last-choice memory for Lookups and the "Yesterday: n" hint: client side, from localStorage and My entries. No API change.
- Theme "Match phone": client only.
- Reports "Next" dates: client side from the existing schedule settings; expose the send time if the client doesn't have it yet.
- Command palette: client side, from the forms list and the user's role.

---

## 10. Strings

- All new strings are in `i18n-additions.json` (en + ar), grouped into new namespaces (`nav`, `today`, `steps`, `lookup`, `sent`, `offline`, `entries`, `records`, `builderV2`, `usersV2`, `assignmentsV2`, `reportsV2`, `overviewV2`, `settingsV2`). Merge them and keep ar/en parity at 100% (the parity check from the September audit must still pass).
- Reuse existing keys wherever they already say the right thing (`formRenderer.*`, `assignments.*`, `fieldRules.*`, `errors.*`, `userManagement.*`, `reports.*`, `executiveOverview.*`). The JSON only adds what's missing.
- Arabic plurals use i18next's `_zero/_one/_two/_few/_many/_other` suffixes; the file includes them where counts appear.
- Field labels and option labels are data (section 9.1), not i18n strings.
- No hard-coded user-facing text in components, including `aria-label`s (this fixes the English-only aria-labels on the language/mode toggles from the September audit).

---

## 11. Build order

Each phase ends with: frontend build passes, `dotnet test` passes (when the backend changed), a check on staging at 390×844 and 1440×900 in ar-light, ar-dark, en-light and en-dark, then a short report to Aamer. Production deploys only with his approval.

1. **Audit and plan:** map every section above to real files and components; list anything that conflicts with this spec. Send the plan before coding.
2. **Foundation:** `tokens.css`, font weights, remove uppercase headings, icons, focus styles; the base components from section 6 (buttons, chips, segments, stepper, inputs, errors, pills, cards, bottom sheet/dialog, drawer, table).
3. **Shells:** phone tab bar plus the Forms tab list; desktop side menu (with the Administration group and the command palette); routes and redirects (`/assignments` → `/`, `/admin/assignments/status` → `/admin/assignments`, new `/entries`).
4. **Sign in, Today, Settings, Sent** (01, 02, 07, 09, 10).
5. **Form renderer:** control mapping (section 7), conditional blocks, lookup sheet, attachments, single-step mode (03–06 with every form as one step), desktop Fill layout.
6. **Backend 9.1 + 9.2**, then the builder (21), then multi-step filling with drafts and review.
7. **Backend 9.3 + 9.4**, then My entries (08) and the Records tab + drawer (20).
8. **Admin pages:** Users (22), Assignments (23, with 9.5), Reports (24), builder home, Overview restyle (25).
9. **Offline queue** (9.6, screen 12).
10. **Polish:** motion, reduced motion, empty/loading/error states on every screen, the acceptance checklist below.

---

## 12. Acceptance checklist

- [ ] Each of the 18 screens matches its PNG at the stated size in its shown language and mode, and looks correct in the other three combinations.
- [ ] At 375px wide nothing overflows sideways except deliberately scrolling tables and chip rows.
- [ ] RTL and LTR mirror correctly: tab order, chevrons, arrows, steppers, progress bar, drawers, side menu.
- [ ] No screen mixes languages in UI text. Data in the other language (e.g. Arabic delay causes on the English dashboard) is wrapped with its own `dir`/`lang`.
- [ ] Every tappable element is ≥ 44×44; phone inputs are 56px and ≥ 16px text (no iOS zoom).
- [ ] Keyboard: every action reachable; Esc closes sheets, dialogs and drawers; focus is trapped in modals and returns to the trigger.
- [ ] Screen readers: radio semantics on chips and segments; labelled icon buttons; grid cells and charts have text alternatives.
- [ ] Text contrast ≥ 4.5:1 in both modes (use the tokens; don't invent colours).
- [ ] Drafts survive closing the tab and reopening; the offline queue survives a reload; a repeated idempotency key never creates a second record.
- [ ] PM role: sees Today, the daily progress report, My entries (own rows only, enforced by the API) and Settings. No admin pages, other people's records, dashboard or builder, and direct URLs return 403.
- [ ] Existing behaviour unchanged: field rules, conditional-required, Arabic digits, soft delete, sign-in rate limit, share link, security headers.
- [ ] Locale files keep 100% key parity; build and all tests pass.

---

## 13. Out of scope — don't change

- Database, API host, deploy pipeline, auth model, CSP/security headers (beyond allowing anything new the UI genuinely needs, flagged first).
- Executive overview calculations and placeholder planned durations.
- Workflow designer UI (still not built; only render workflow status where it already exists).
