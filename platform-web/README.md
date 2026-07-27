# Platform web — frontend starting point

This is the one piece worth building carefully by hand: `FormRenderer.tsx`, a single
component that renders a submission form for ANY published form, driven entirely by its
field metadata. No per-form code exists anywhere in here — that's not an accident, it's
the actual point of a low-code platform. `App.tsx` is a deliberately thin shell that
proves the renderer works against the real API; it is not the real app.

**This has been verified to actually compile** (`npm run build` succeeds, strict
TypeScript, zero errors) but has NOT been tested against a real running instance of the
API - that needs an environment that can actually reach it, which this one can't. Treat
the first real run against your live API as the actual test, not this build success.

## Getting it running

```
npm install
npm run dev
```

Create `.env.local` with:
```
VITE_API_BASE_URL=http://localhost:5080
```
(or wherever the API actually ends up reachable).

## What's real vs. what's a placeholder

**Real and wired up:**
- Login (`/api/auth/login`)
- Fetching a form definition and its published fields (`/api/forms/{id}`)
- Rendering the correct input for every `FieldType` (short text, long text, number,
  decimal, boolean, date, dropdown, lookup) - genuinely generic, not a switch statement
  with 7 form-specific branches hiding behind it
- Lookup fields fetch their target form's records live and populate a dropdown
- Submitting data (`POST /api/forms/{id}/submissions`) and re-listing records after

**Explicitly not real yet:**
- **No form picker / navigation.** There's no `GET /api/forms` (list) endpoint on the
  backend yet, so `App.tsx` asks for a form ID by hand. Add that endpoint first, then
  build a real nav around it - straightforward addition, `ApplicationDbContext.FormDefinitions`
  already has everything needed.
- **No refresh-token flow.** The access token is stored and used, but nothing refreshes
  it - it'll silently start failing 30 minutes after login. `POST /api/auth/refresh`
  doesn't exist on the backend yet either.
- **Attachment fields render as a disabled placeholder**, honestly labeled as not wired
  up - there's no File Management module yet for it to talk to.
- **Lookup display labels are a guess.** They use the first `ShortText` field on the
  target form as a stand-in label, because the backend doesn't expose a designated
  "display field" for a form yet. Works fine for Materials (`item_code`) and Locations
  (`location_name`) since those happen to be first, but it's a convention, not a rule -
  worth formalizing with a real `DisplayFieldCode` on `FormDefinition` before this goes
  much further.
- **No form builder UI.** This renders forms; it doesn't let an admin create or edit one
  visually - that's still done via the API directly or the PowerShell seed scripts.

## Suggested next steps, in order

1. Add `GET /api/forms` to the backend (list all forms, maybe filtered by module) -
   small, unblocks everything else.
2. Build a real app shell: sidebar/nav listing forms by module, routing (React Router),
   layout that isn't just a centered column.
3. Wire up token refresh so sessions don't silently die after 30 minutes.
4. Only after those: a visual form builder, if that's still the priority over Workflow
   Engine per the roadmap.
