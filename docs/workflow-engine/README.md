# Workflow Engine — Phase 3 delivery

New files only - see `INTEGRATION.md` for the four small, precise additions needed to
existing files. Written as intent + exact snippets rather than blind full-file
replacements, since those files have moved since I last saw them (your own bug fixes).

## What's real here

A working state-machine engine: define states and transitions for a workflow attached to
one form, gate each transition to specific roles, publish it, and from then on every
submission to that form automatically starts a workflow instance at the initial state.
Executing a transition checks the caller's roles against who's allowed, moves the state,
and records an append-only history entry - the actual audit trail an approval process needs.

Not compiled or run anywhere - same honest limit as Phase 0's first delivery. Written
carefully, following the exact patterns already established in the Identity and Form
Engine code, but `dotnet build` is the real test, not this note.

## Deliberate scope cuts, not oversights

- **No workflow versioning.** Publish is final - editing a published workflow isn't
  supported. Retire it and create a new one if it needs to change. FormDefinition has real
  versioning; WorkflowDefinition deliberately doesn't, to keep this increment scoped.
- **One published workflow per form**, enforced at publish time. Multiple approval paths
  for the same form (e.g. different flows for different record types) isn't supported.
- **No conditional transitions.** Every transition is a manual action gated by role only -
  no "auto-approve if quantity under X" business rules. That's a reasonable next step
  once real usage shows which rules actually matter.
- **No notifications.** The original roadmap bundled "Workflow Engine + Notifications" as
  one phase; this delivery is Workflow only. A transition executing doesn't alert anyone -
  worth treating as its own follow-up, not assuming it's covered.
- **No frontend for any of this yet.** Pure API, same starting point as Phase 0 was before
  FormRenderer existed. The natural next frontend piece: a status badge + action buttons
  on top of the existing SubmissionsTable, driven by `GET /api/records/{id}/workflow`.

## Suggested first real test

Attach a workflow to Stock adjustment, since it's the form you've already tested twice:
`Draft → Pending approval → Approved` / `Draft → Pending approval → Rejected`, with the
approval transitions gated to the Administrator role. Submit one, then try approving it.
