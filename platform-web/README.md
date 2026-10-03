# platform-web — the real frontend

**This is the real, live app — not a prototype.** An earlier version of this file
described `App.tsx` as "a deliberately thin shell" proving a form renderer works, with no
nav, no refresh-token handling, no form builder, and attachments rendering as a disabled
placeholder. None of that is true anymore: all of it has since been built, and this app
is what's actually running at **https://asasksa.co**. See the root `README.md` and
`CLAUDE.md`/`docs/PROJECT_STATUS.md` (one level up) for the full project picture — this
file just covers getting the frontend running.

## What's here

- Real routing (React Router) with a sidebar nav (`FormPicker.tsx`) grouping live forms
  by module, driven entirely by `GET /api/forms` — no hardcoded form IDs anywhere.
- `FormRenderer.tsx` — renders a submission form for **any** published form, driven
  entirely by its field metadata. No per-form code exists anywhere in this app; that's
  the actual point of a low-code platform's frontend. Handles every `FieldType`
  (short/long text, number, decimal, boolean, date/time, dropdown — including
  dynamic-sourced dropdowns, lookup — including filtered/cascading lookups, attachment),
  plus conditional field visibility (`isFieldVisible` — has to stay logically identical
  to the backend's own `SubmissionValueValidator.IsFieldVisible`, see CLAUDE.md).
- `FormBuilder.tsx` / `BuilderHome.tsx` / `AddFieldForm.tsx` / `FieldEditorRow.tsx` — the
  real visual Form Builder: create a form, add fields, publish, and live-edit a
  published form's fields (relabel, reorder, add, rename code, change type, archive,
  permanently delete), plus the Access panel for role/user-based restriction.
  `resolveEditableFields` picks draft-vs-published the same way the backend's
  `FormEditTargetResolver` does — these two have to keep agreeing, see CLAUDE.md.
- `RequireAdmin.tsx` — route guard redirecting non-admins away from `/builder` and
  `/admin/users`.
- `UserManagement.tsx` — create accounts, assign/edit roles, deactivate/reactivate users.
- Full refresh-token handling — sessions don't silently die 30 minutes after login.
- File Management — upload/list/view/delete attachments, including attaching inline
  while filling out a form.
- Workflow status/approval UI — role-gated action buttons on a submission's Workflow
  panel.
- Full bilingual Arabic/English UI (`react-i18next`), RTL layout throughout, persisted
  language choice.
- Dark/light mode toggle ("Structural steel" visual design) alongside the language
  toggle, both built on a shared `Switch.tsx` component.

## Getting it running

```
npm install
npm run dev
```

Create `.env.local` with:
```
VITE_API_BASE_URL=http://localhost:5080
```
(or wherever your local API instance is actually reachable — see the root `README.md`
for backend setup; it talks to the same Railway Postgres production uses, not a local
database).

Dev server runs at `http://localhost:5173`.

## Known, deliberate gaps (not oversights — see root CLAUDE.md for the full reasoning)

- No admin-UI panel yet for configuring a Lookup field's filter or a Dropdown's dynamic-
  options source from the Form Builder screen — both are set via the API/PowerShell
  scripts today. Worth adding before the next form that needs either is built by hand
  through the builder instead of a script.
- No workflow-designer UI — workflows are still created via API/PowerShell scripts.
- Lookup display labels are a guess (first `ShortText` field on the target form) — there's
  no designated `DisplayFieldCode` on `FormDefinition` yet.

For anything architectural — why forms work this way, what's verified against the real
production database, what's next — read the root `CLAUDE.md` and
`docs/PROJECT_STATUS.md`, not this file.
