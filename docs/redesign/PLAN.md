# Redesign build plan — phase 1 (audit), 2026-11-06

> **Status 2026-11-07: all ten phases done and on staging** (part of Asas v2.0 - full record and
> production checklist in `docs/releases/ASAS-v2.0.md`). This file keeps the original audit (sections
> 1-5) and the exact schema/API change per phase (section 6).

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

## 6. Phase log — exact schema/API changes per phase (owner: "do the rest of the phases yourself", 2026-11-07)

### Phase 6 — done 2026-11-07 (approved by the owner before starting)
Migration `AddFormStepsAndEnglishLabels` (adds `FormDefinitions.NameEn`, `FieldDefinitions.LabelEn` + `StepId`, table `FormSteps`); endpoints in CLAUDE.md "Steps and English labels". No `Data_*` table or `Report_*` view touched.

### Phase 7 — My entries (08) + Records (20)
**No schema change, no migration.** Read-only queries plus one response superset:
- `IDynamicDataRepository.SearchAsync(table, fields, RecordSearch)` (new; `QueryAsync` unchanged): equality filters, `ILIKE` text search over text/dropdown columns (escaped), a date range on a DateTime field or on `CreatedAtUtc`, own-rows (`CreatedByUserId`), and a `(CreatedAtUtc, Id)` cursor. Every identifier through `SqlTypeMapper.AssertSafePostgresIdentifier`, every value a parameter. Rows also carry `CreatedAtUtc` and `CreatedByUserId`.
- `GET api/forms/{id}/submissions` (administrators, unchanged route): new optional `q`, `from`, `to`, `dateField`, repeatable `f=code:value`; each item gains `createdAtUtc`, `createdByUserId`, `createdByName`, `lookupLabels` (resolved Lookup labels — fixes the raw-GUID display) and `attachmentCount`. Old fields unchanged, so old callers keep working.
- `GET api/forms/{formId}/submissions/{recordId}` (new): owner-or-administrator **and** `FormAccessChecker.HasAccess` for the record's form (owner decision 2026-11-06). Returns the row, sender name, lookup labels and its attachment list. 403 otherwise.
- `GET api/submissions/mine?formId=&before=&pageSize=20` (new, any signed-in user): the caller's own non-deleted rows across forms they can access, newest first; `formId` of a form they can't access → 403.
- Tests: 403 for a user without form access on both new endpoints; another user's record → 403; own rows only.

### Phase 8 — admin pages (22-25)
**No schema change, no migration.**
- `POST api/form-assignments/remind-pending` (administrators): reminds everyone still pending today through `AssignmentNotifier`. Logged as kind `manual`, so the existing send-once index makes it at most once per person, form and day per channel, separate from the automatic `reminder-{slot}` kinds (answer to owner decision 7: the automatic reminder never counts against it). Returns `{sent, failed, alreadyReminded, noChannel}`.
- `AssignmentDayDto` gains `recordId` (that day's first entry, so a grid cell opens it) and `lastRemindedAtUtc` ("Reminded at 4:10 PM"); `SubmissionStamp` carries the row id. Additive only.

### Phase 9 — offline queue (§9.6, screen 12)
**One migration, `AddSubmissionReceipts`: a new plain table only** (no `Data_*` table or `Report_*` view touched, per the owner's phase-6 condition):
`SubmissionReceipts(Id, UserId, FormDefinitionId, IdempotencyKey varchar(64), RecordId uuid NULL, EnteredAtUtc timestamptz NULL, CreatedAtUtc, …audit)`, unique index `(UserId, IdempotencyKey)`, index `RecordId`.
- `POST api/forms/{id}/submissions` accepts an optional `Idempotency-Key` header (8-64 chars `[A-Za-z0-9_-]`) and optional `X-Entered-At` header (ISO time from the phone). With a key, the receipt row is inserted **before** the data row (the unique index is the "only once" lock, like `NotificationLog`); a repeat of the same key by the same user returns the original record id (200) - or 409 `submission.inProgress` if the first one is still being written. Repeats are honoured for 7 days; the row itself is kept, because it also holds the entered-at time.
- **Entered-at (owner decision 8):** `CreatedAtUtc` stays the server's time. The phone's time is stored on the receipt and used (a) for the assignment day (`FormAssignmentTracker`, stamps window widened by 3 days so a late-sent entry still lands on its day) and (b) for display (`RecordItemDto.enteredAtUtc`). Validated on the server: not more than 5 minutes in the future (clock skew), at most 3 days old - otherwise 400 `submission.enteredAtInvalid`.
- Client: entries sent with no connection (or a network failure) are kept in IndexedDB with their key, entered-at and photos and sent on `online`, on app open, every 60 s while queued and on "Try sending now"; photos upload after the record. Background Sync is **not** used: the access token lives in localStorage, which a service worker can't read - adding that would mean moving tokens, a separate decision.

### Phase 10 — polish (motion, states, acceptance checklist)
**No schema or API changes.** Client only:
- `LoadingState` (announced "Loading…") and `ErrorState` (what failed + "Try again", recovers without a reload) in `components/ui/Surface.tsx`, used by every page that loads data; Today shows an error instead of spinning forever when the person/assignments can't load; the assignments grid says when its days couldn't load instead of drawing them blank; a failed "load more" on My entries no longer retries in a loop.
- Reduced motion: the loading mark stands still (its animation ends on a faded frame, so cutting it short hid it).
- Phone (< 1024px): every button/link/radio/tab/switch gets an invisible ::after hit area of at least 44×44 (drawn sizes unchanged); inputs/selects are at least 44px tall; the assignments grid uses 44px cells there and scrolls sideways; "Stop" is always visible (no hover on a phone).
- Data in the other language is isolated (`<bdi>`): chip/segment labels, the field name in "Choose …", the records date header, role descriptions; language names carry their own `lang`/`dir`.

### After phase 10 — fixes from the owner's checks on staging (2026-11-07)
**No schema change.**
- `GET api/form-assignments` returned 500: `GetFormAssignmentsQuery` passed an open range up to `DateOnly.MaxValue - 1` and phase 9's "+1 + 3 late days" overflowed. The list now reads up to today and `FormAssignmentTracker.AddDaysClamped` caps the date. Two regression tests run the real handler.
- "Assign a form" card full width below 1024px (its 380px cap applies from desktop width only).
- WhatsApp switched on (`WHATSAPP_READY = true`) after Meta approved the templates; "Connect" placeholders replaced by status lines (messages go to the number on the account).
