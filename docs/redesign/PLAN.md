# Redesign build plan — phase 1 (audit), 2026-11-06

Branch `feature/redesign`, cut from `feature/stakeholder-reports` (what staging runs). Handoff
copied to `docs/redesign/` as instructed. All 18 PNGs and HANDOFF.md read in full.

## 1. Handoff section → real files

| Handoff | Where it lands today | Plan |
|---|---|---|
| §4 tokens, font, no uppercase, focus | `platform-web/src/index.css` (`:root` blocks), `tailwind.config.js` (colour keys = CSS vars), `index.html` (Google Fonts URL, no 800) | Replace both `:root` blocks with `tokens.css`; add new vars to Tailwind; add 800; strip `uppercase`/`tracking-*` from headings app-wide; global `:focus-visible` rule. Retire `--signin-texture` and `shadow-recessed` uses. |
| §4 icons | none (no icon library; ad-hoc SVGs in `Layout.tsx`, `Settings.tsx`) | New `components/icons.tsx`: the inline SVG paths from `screens/html`, `currentColor`, RTL flip for directional ones. No new dependency. |
| §5 shells | `routes/Layout.tsx` (sidebar + phone hamburger drawer), `components/FormPicker.tsx` | Rewrite `Layout.tsx` into `PhoneShell` (<1024px, bottom tab bar) and `DesktopShell` (≥1024px, 264px side menu + Administration group + command palette). New `routes/FormsList.tsx` (Forms tab). |
| §6 components | scattered Tailwind in each route; `Switch.tsx`, `DateField.tsx`, `LoadingSpinner.tsx`, `StatusLed.tsx`, `AssignmentStatus.tsx` | New `components/ui/` set: Button, Chip/ChipGroup, YesNo, Segmented, Stepper, DateQuickPick (wraps existing `DateField` picker), LookupRow + BottomSheet/Dialog, Card, Pill, Table, Drawer, EmptyState, InfoBanner, DayCell, FieldShell (label/hint/error). |
| §7 control mapping | `components/FormRenderer.tsx` (616 lines, one long column), `forms/fieldValues.ts` (visibility, `checkValue`) | Rebuild the renderer on the §6 components; keep `fieldValues.ts` logic (visibility, rules, Arabic digits) unchanged and reuse it. |
| 01 Sign in | `routes/SignIn.tsx` | Restyle; same auth calls and `signIn.*` errors. |
| 02/10 Today | `routes/MyAssignments.tsx`, `routes/Welcome.tsx` | New `routes/Today.tsx` at `/`; `/assignments` → `/`. Uses `api.assignments.mine` + `api.forms.list`. |
| 03–07, 11 filling | `routes/FormView.tsx` + `FormRenderer.tsx` | Phone: stepped filling, drafts (localStorage + IndexedDB for files), review, Sent. Desktop: Fill tab, one card per step. |
| 08 My entries | — | New `routes/MyEntries.tsx` + detail page; needs §9.3 API. |
| 09 Settings | `routes/Settings.tsx`, `components/ChangePasswordForm.tsx`, `TelegramConnect.tsx`, `theme/mode.ts`, `public/boot.js` | Restyle; add "Match phone" (third mode). `/account` comes back as the change-password page. |
| 12 Offline | — | New queue (`offline/queue.ts`, IndexedDB), service worker for Background Sync, screen 12; needs §9.6 API. |
| 20 Records + drawer | `routes/FormView.tsx`, `components/SubmissionsTable.tsx`, `AttachmentsPanel.tsx`, `WorkflowPanel.tsx` | Fill/Records tabs in URL; filter row; column picker; drawer reusing attachments/workflow logic; needs §9.4 API. |
| 21 Builder | `routes/FormBuilder.tsx`, `components/FieldEditorRow.tsx` (701 lines), `AddFieldForm.tsx`, `routes/BuilderHome.tsx` | Three panes; existing dialogs (rename code, change type, rules, visibility, lookup filter, archive/delete) become the inline settings cards — same API calls. Needs §9.1/9.2. |
| 22 Users | `routes/UserManagement.tsx`, `UserPhonePanel.tsx`, `UserTelegramPanel.tsx` | Role cards, table, drawer; same API calls. |
| 23 Assignments | `routes/FormAssignments.tsx`, `routes/AssignmentTracking.tsx` | Merge into one page; `/admin/assignments/status` → `/admin/assignments`. Grid from the existing `GET form-assignments/days`; new "remind pending" endpoint. |
| 24 Reports | `routes/StakeholderReports.tsx` (built today) | Restyle; schedule cards; "Show reason" dialog instead of raw errors. |
| 25 Overview | `routes/ExecutiveOverview.tsx`, `ExecutiveOverviewPanels.tsx`, `ExecutiveOverviewCharts.tsx` | Restyle only; plan-met chart becomes bars. Calculations untouched. |
| §10 strings | `i18n/locales/en.json`, `ar.json` (633 keys each, 100% parity) | Merge `i18n-additions.json` (14 new namespaces, no collisions). |

## 2. Backend work (§9)

| Item | Change |
|---|---|
| 9.1 English labels | Nullable `FormDefinition.NameEn`, `FieldDefinition.LabelEn`; dropdown options gain `labelEn` inside `OptionsJson` (no column); DTOs return both; `FormVersion.CreateDraftFrom` copies them (CLAUDE.md sync rule). Migration. |
| 9.2 Steps | New `FormStep` (FormVersionId, Order, TitleAr, TitleEn) + nullable `FieldDefinition.StepId`; CRUD/reorder commands; `CreateDraftFrom` copies steps. Migration. Daily report steps seeded on staging by a reviewed script. |
| 9.3 My entries | `GET api/submissions/mine?formId=&cursor=` (own rows only, every role); `GET api/forms/{id}/submissions/{recordId}` owner-or-admin (**there is no single-record GET today** — the handoff assumes one). |
| 9.4 Records filters | Extend the submissions list with date range, Lookup/Dropdown/Boolean filters and text search, all through `SqlTypeMapper.AssertSafePostgresIdentifier` + parameters; resolved Lookup labels (fixes the raw-GUID bug). |
| 9.5 Grid | Existing `GET form-assignments/days` covers the grid; new `POST form-assignments/remind-pending` (today, Telegram/WhatsApp via `AssignmentNotifier`). |
| 9.6 Offline | `Idempotency-Key` header on submission create (new small table, 7-day expiry) + client created-at (validated: not future, ≤ 3 days) used as the row's `CreatedAtUtc`. |

## 3. Conflicts and gaps — need a decision

1. **Today card sub-line:** PNG 02/10 says "Daily reports · about 3 minutes"; spec and `i18n-additions` say "{{module}} · {{n}} steps". → Plan: follow the spec/strings (steps). Minutes have no data source.
2. **No single-record GET endpoint** exists for "reuse the existing detail endpoint" (§9.3/08). → Plan: add one (owner-or-admin).
3. **No i18n parity check exists** in the repo (the "September audit" one). → Plan: add a small check script (`npm run i18n:check`) that compares base keys (Arabic's extra plural forms allowed).
4. **`/account` currently redirects to `/settings`** (since 2026-10-21); the spec wants it back as the change-password page. → Plan: restore it.
5. **Executive overview stage is 1760×990 today; spec says a 1600×900 design.** Both 16:9 and scaled to fit. → Plan: keep 1760×990 (no visible difference after scaling) unless you want it changed.
6. **Default theme:** today dark; spec makes "Match phone" the default for new users. → Plan: as spec (existing users keep their saved choice).
7. **"Remind pending" repeat presses:** the reminder log allows one message per assignment/kind/day. → Plan: a manual reminder can go once per person per day; the button then shows "already reminded".
8. **Queued entry's day:** using the phone's created-at as the row's `CreatedAtUtc` changes what that audit column means for those rows (client time, not server time). → Plan: as spec (validated to ≤ 3 days, not future), noted in CLAUDE.md.
9. **Local test commands** in the handoff are Windows (`npm.cmd`, `src\Platform.Api`); this machine is a Mac. → I'll give Mac commands.
10. **CLAUDE.md** records the 2026-10-21 "sidebar = forms + Settings" decision; decision 1 reverses it. → I'll update CLAUDE.md in phase 3.

## 4. Files that need your OK before I touch them (handoff §0.6)

- **Database/API contract** (both sides depend on them): the two migrations (9.1/9.2 and the idempotency table), `FormDtos`/`types.ts` shape changes, `SubmitFormDataCommand`, `IDynamicDataRepository` + `DynamicDataRepository` (filters, own-rows query, created-at), `FormVersion.CreateDraftFrom`.
- **`index.html`** (font URL) and **`public/boot.js`** (third theme value).
- No `appsettings*.json` change and no CSP change is needed (Google Fonts already allowed; the service worker is same-origin).

## 5. Order (handoff §11), each ending with build + tests + staging check at 390×844 / 1440×900 in ar/en × light/dark, then a report

2 Foundation → 3 Shells → 4 Sign in/Today/Settings/Sent → 5 Renderer (single-step) → 6 Labels+Steps API, builder, multi-step+drafts → 7 My entries + Records API and pages → 8 Users, Assignments, Reports, Builder home, Overview → 9 Offline queue → 10 Polish + acceptance checklist.

Phases 2–5 are frontend only. Backend work starts in phase 6.
