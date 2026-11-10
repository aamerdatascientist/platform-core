# Stage-based progress: audit and plan (awaiting approval)

Status: **built and on staging (2026-11-10, `feature/stage-progress`)**, not in production. Plan date: 2026-11-10.
Branch: a new `feature/stage-progress` cut from `feature/redesign` (which today equals production `main`, v2.0), deployed by pushing it to `staging`.

---

## 1. Audit: what exists today

### 1.1 The redesign
The approved UI redesign (`docs/redesign/HANDOFF.md`) is **finished and live on production since 2026-11-09 (Asas v2.0, `f1bab5d`)**. It is not in flight, so nothing has to be merged around it. The repeatable section is built on the redesigned renderer (`components/FormRenderer.tsx`), builder (`components/builder/*`), records drawer and offline queue. It must keep every redesign rule: AR/EN labels, RTL, 44 px targets on phones, error and loading states, and the offline queue with idempotency keys.

### 1.2 Form engine: what the task can use as-is
- **Field types:** ShortText, LongText, Number (int), Decimal, Boolean (Yes/No), DateTime, Dropdown, Lookup, Attachment.
- **Visibility:** a field can depend on *one sibling field's value*.
- **Lookups:** can be narrowed by the project picked on the form (`FilterByFieldCode`); `project-floors` already does this.
- **Dropdown options:** can be fed from another form's submitted values.
- **Validation, server-side:** required, min/max, no future dates, unique, Arabic digits, size limits, plus a check that the picked option still exists.
- **Steps (presentation only):** the daily report already has 3 (Basics / Progress / Photos).
- **Storage:**
  - Each form has one physical table (`Data_*`) plus a reporting view.
  - A new version adds columns; archiving a field keeps its column and its old values.
  - Project managers already load dropdown options through `GET api/forms/{id}/lookup-options` without access to the master-data forms.

### 1.3 Missing pieces the task needs

| Needed | Status |
|---|---|
| Repeatable section (child rows) | **missing**, as the task says |
| Multi-select (floors, sides) | **missing**: no multi-select field type |
| Calculated read-only field (buildable area) | **missing** |
| Lookup showing only some rows (Active projects, active stages) | **missing**: lookups can only be narrowed by a sibling field |
| Fields that show or hide based on the *chosen stage's* settings (has sub-steps, per floor or per side, level scope) | **missing**: visibility can only test a field's own value, not a property of the row it points to |
| Server logic that runs when a particular form is saved (create floors and plan copy, eligibility, pause rules) | **missing**: `SubmitFormDataCommand` is fully generic, with no per-form hooks |
| Saving parent and child rows in one transaction | **missing**: the row insert is one Dapper statement |
| Locking a form's fields so admins can't break a calculation | **missing**: any admin can rename or archive any field |

### 1.4 The current data (production)
- **Projects:** 1 project (`119`, Makkah, started 2026-01-20, `total_floors` = 6, status `active`).
- **Daily reports:** 283 on the phase model, plus 6 floor, 2 footing and 1 zone rows.
- **Projects form codes:** `project_code`, `project_name`, `client_name`, `city`, `project_type`, `status` (planning / active / on_hold / completed / **cancelled**), `project_manager`, `total_floors`, `start_date`, `expected_completion`, `notes`.
  - The task's `code` and `name` already exist as `project_code` and `project_name`, and `planned_start_date` overlaps `start_date`.
- **Daily report v1:** report_date, project, crew_count, weather, phase, milestone, zone, footing, floor, mep_trade, mep_room, had_delay, delay_cause, has_problem_today, problem_description, plan_completed_today, site_photo.

### 1.5 Corrections to the task's background
- **Placeholder days:** `OverviewProgressPlan` is zone **18**, footing **14**, floor **17**, MEP **6 days × 3 trades** per floor, milestones **10/8/5/3**, plus a **10% slack** on planned end dates (owner, 2026-11-03). The task says zone 2, footing 2, floor 8, trade 3, milestones 4/3/2/1. The real values don't change the plan, but the 10% slack needs a decision (Q8).
- **Other users of the old phase model** that the task doesn't list:
  - Stakeholder reports (`ReportAnalysis`, `StakeholderReportBuilder`: the PDF tables and the AI analysis prompt)
  - The overview's forecast finish, days late, the lateness KPIs and the stage filter
  - `scripts/go-live/apply-field-rules.ps1`
  - All of them have to move to the new model in this same change.
- **Friday off is already hard-coded in three places:** the overview, the stakeholder reports and the form-assignment reminders. All three move onto the new calendar service.

### 1.6 Reference totals: checked before planning
Computed from the seed rows (scratch script):
- **Walls start days:** 21, 31, 41, 51, 61, 70; structure + walls = **78**.
- **Skeleton:** **170** / **194**.
- **Finishing:** **158** / **369**.
- **Whole project:** **328** / **563**.

All match the task, **on one condition**: structure and walls use the overlap rule (78 days) in *both* the fastest and the slowest totals. They are *not* treated as a fastest/slowest range, even though they are per-floor stages. The plan treats them that way (see Q9).

---

## 2. Design

### 2.1 Recommended split: generic engine pieces plus one named domain module
The stage block depends on *plan data*: which stages apply, their sub-steps, whether they're per floor or per side, and which levels they cover. The generic engine can't express all of that without growing a rules language. Recommendation:

**A. Generic platform capabilities.** Reusable by any form, managed in the builder:
1. **Repeatable section**
   - A group of fields with a code, AR/EN title, and min/max blocks.
   - Stored in a child table `Data_<Form>__<section>` (`Id`, `ParentId` → parent `Id`, `BlockIndex`, the block's columns), soft-deleted with its parent.
   - Validation, required-when-visible, field rules, Arabic digits and size limits run *per block*.
   - Visibility inside a block can refer to fields in the same block or in the header.
   - The parent and its blocks are saved in **one transaction**, together with the idempotency receipt.
2. **Multi-select field type**
   - Static options, or a Lookup with multiple picks.
   - Stored as `text[]` / `uuid[]`.
   - Validated per item: the option or row must exist, and must pass the lookup filter.
3. **Row filter on a Lookup**
   - Show only rows where field X is one of the given values, for example `status ∈ {active}` or `is_active = Yes`.
   - Checked on the server too: an inactive row can't be submitted even through a crafted request.
4. **"Visible when the chosen row's field X is one of …"**
   - An extension of today's visibility rule, for Lookup controllers. For example, "floors" shows when the chosen plan row has `duration_type ∈ {per_floor}`.
5. **Calculated number field**
   - Read-only.
   - A small formula of sibling number fields and constants (`+ − × ÷`), for example `plot_area_m2 * buildable_pct / 100`.
   - Shown live in the form and *computed and stored by the server*; the client's value is ignored.
6. **System form lock**
   - A form marked "system" can't have its fields renamed, retyped or archived in the builder, though labels can still change.
   - Applied to the forms the calculation reads: projects, project-floors, the two stage plans, the daily report and the pause log. Otherwise one builder edit could silently break progress.

**B. The "Stage progress" module (server, domain-specific).**
- **One registry of hooks keyed by form code:** before-validate, after-insert and after-update, called from `SubmitFormData` and `UpdateSubmission` *inside the same transaction*. Forms without a hook behave exactly as today.
- **Hooks:**
  - `projects` (on create):
    - Generate the levels into `project-floors`.
    - Copy the default plan into `stage-plan-project`.
    - Compute `buildable_area_m2`.
  - `daily-progress-report`:
    - Project must be Active, not paused.
    - The stage must apply to this project and be active, and the sub-step must belong to it.
    - Floors and sides must be within the stage's level scope or side count.
    - No duplicate stage + sub-step in one submission.
    - The unit must not already be finished unless reopened.
    - Sub-steps must start in order on the unit.
    - A per-project advisory lock makes two foremen submitting at the same moment behave as if one went first.
  - `project-pause-log`:
    - No pause when already paused, no resume when active.
    - Updates the project's status in the same transaction.
- **Stage-plan options for the daily report.** These come through the *existing* lookup-options endpoint, so project managers need no access to the plan forms:
  - Stages: active, applies_when resolved against the project, in sort_order.
  - Sub-steps: of the chosen stage.
  - Floors: filtered by level scope.
  - Sides: 1…N.
- **`WorkingDayCalendar` service:** the one place for date arithmetic.
- **Calculators:**
  - Units, days earned, planned schedule and ranges, elapsed and variance.
  - They replace `OverviewProgressPlan` in the overview and the stakeholder reports.

Why this split: (A) is what the task asks to be generic. The stage rules in (B) are business logic, and a generic rules language would be harder to test and easier to break. The admin still edits plans, projects and pauses through the normal records screens.

### 2.2 Forms (all AR + EN labels; "system" locked)

**1. `projects` (new version of the existing form; existing records keep working)**
- **Kept unchanged:** `project_code`, `project_name`, `status` and `start_date`.
  - The task's `code` and `name` *are* these fields.
  - `start_date` is relabelled "Planned start date / تاريخ البدء المخطط" and made required for new records.
  - Renaming codes would rename live columns and break every reference, so I won't.
- **Added:** `location` (or reuse `city`, Q10), `plot_area_m2`, `buildable_pct`, `buildable_area_m2` (calculated), `parking_levels`, `apartment_floors`, `apartments_per_floor`, `walls_per_floor`, `needs_deconstruction`, `needs_support` and `outside_sides`, with the task's defaults and limits.
- **Kept but no longer used for progress:** `total_floors` and `expected_completion`. The planned finish is now computed.
- **Status:** `cancelled` stays as an option, since old data may use it. English labels are added to every option.

**2. `stage-plan-default`** (admin only)
- The fields as specified, plus 36 seed rows.
- `sort_order` must be unique; `days` must be a whole number above 0.
- Two **reserved stage codes**, `structure` and `walls`, drive the overlap rule and the single "level" field. Alternative: a `schedule_rule` column (Q9).

**3. `stage-plan-project`**
- Form 2's fields plus `project`, filled in by the copy.
- Editing goes through the records screens for admins.
- "Delete" of a row that has reports is refused; deactivating (`is_active` = No) is the way to retire a stage.

**4. `daily-progress-report` (version 2 of the same form)**
- **Same form id and table**, so the following keep working with no data move:
  - form assignments, reminders and Telegram/WhatsApp messages
  - My entries, Records and old records
- **Header kept:** report_date, project (row filter: status = active), crew_count, weather, had_delay → delay_cause, site_photo.
- **Archived** (columns and old values kept, shown on old records):
  - phase, milestone, zone, footing, floor, mep_trade, mep_room
  - has_problem_today, problem_description, plan_completed_today. These three move *into* the block, under new codes so old values stay readable.
- **New repeatable section `stages`** (min 1, max 10, Q11), with these fields:
  - stage, substep, level, floors, sides
  - plan_met, finished, has_problem, problem_description
- Stored values are `stage_code` / `substep_code` text, the floors' row ids, and side numbers 1…N. These keep a report meaningful even if a plan row is later edited.
- Projects show the "paused" message and a disabled submit when On hold; the server enforces it too.

**5. `project-pause-log`**
- project, action (Pause/Resume), date (not in the future), reason (Government inspection / Other) and an optional note (Q12).
- A **Pause / Resume** button on the project's record drawer opens this form with the project and action pre-filled.

**Floors: `project-floors`, new version.** Adds `level_number`, `level_kind` (parking / apartment), `label_en` and `is_active`. The generated rows:
- **Parking:** "Parking / المواقف", level 0. Several parking levels become P1, P2.
- **Apartment floors:** "Floor n / الدور n".
- A reduction deactivates floors and never deletes them (Q5).

**`project-zones`, `project-footings`:** hidden from the sidebar (a new "hidden" flag on the form; data untouched) and no longer read by any code.

**"Reopen a unit" (admin):** a small additive table `StageUnitReopenings` (project, stage, sub-step, level or side, by, when, reason). It is created from a button in the project's progress view. A reopened unit can be reported again, and its earned days continue from where they were.

### 2.3 Calculations

**Units:**
- Built from the project's plan:
  - **fixed:** one unit
  - **per_floor:** one per active floor in the level scope (all / apartment only / parking only)
  - **per_side:** one per side 1…outside_sides
- Rows whose applies_when doesn't hold are skipped.

**Earned days:**
- Each block with plan_met = Yes adds 1 to each selected unit, capped at the unit's planned days.
- finished = Yes sets earned to planned and the finish date to that report's date.
- The actual start is the first report on the unit.
- Unit %, stage % and project % are Σ earned ÷ Σ planned.
- **One block or separate submissions:** both produce the same per-unit day list, so the results are identical. A unit earns at most 1 per *report date*, so two blocks for the same unit on the same day can't double-count; I'll confirm this with you (Q13).

**When a project's plan days change:**
- Earned days are capped at the new planned days, as the task says.
- **Suggested refinement:** keep the earned days stored uncapped and cap only at display time. Then raising the days again later restores the progress that was already earned, instead of losing it.

**Planned schedule:**
- Stages run in sort_order.
- Structure goes levels in order, back to back. Walls on level i start at max(start of structure on i+2, or the end of structure; end of walls on i−1). Together they take 78 days in both totals.
- Every other per-floor or per-side stage is a **range**: fastest = days, slowest = days × units.
- Each stage's planned start and finish are therefore ranges, and so is the project's finish.

**Calendar:**
- `WorkingDayCalendar` gives `IsWorkingDay`, `AddWorkingDays` and `WorkingDaysBetween`.
- The weekly day off is configurable (default Friday, Q2). Paused periods are excluded.
- A future vacation calendar plugs into the same interface; nothing else changes.

**Elapsed and variance:**
- Elapsed = working days from the actual start to today, minus paused days.
- Variance per unit = actual finish − planned finish, positive when late.
- Ahead / on track / behind waits on Q1.

**Executive Overview:**
- The phase breakdown becomes the **stage breakdown**, and the stage filter lists the plan's stages.
- Work complete, project status, forecast finish, days late and the lateness KPIs use the new numbers.
- The zone/footing/floor/MEP placeholders are deleted.
- **Stakeholder reports:** the same calculator, so the PDF and the AI analysis match the overview.

### 2.4 Database migrations (EF; all additive, no `Data_*` table touched by EF)
1. `FormSections` table (Id, FormVersionId, Code, TitleAr, TitleEn, MinBlocks, MaxBlocks, DisplayOrder, ChildTableName). `FieldDefinitions.SectionId` (nullable).
2. `FieldDefinitions` new columns, all nullable: `RowFilterJson`, `VisibleWhenRowFieldCode` (row-attribute visibility), `FormulaJson` and `AllowMultiple` (or a `MultiSelect` FieldType value). The enum is appended at the end, so existing values keep their numbers.
3. `FormDefinitions.IsSystem`, `FormDefinitions.IsHidden` (bool, default false).
4. `StageUnitReopenings` table.

The `Data_*` changes (new columns, the child table, its reporting view) happen through the existing publish path, the same way every form change does today.

### 2.5 Existing data: proposals, nothing applied without your yes
- **Old daily reports (283 on production):**
  - **273 of them are generated backfill** for project 119 (2026-01-20 → 11-01, tagged `{"backfill":"2026-11-02"}`, see CLAUDE.md). Only about 10 are real client input, from 2026-11-02 on.
  - Kept and readable exactly as now: the records list, the drawer, and v1 fields shown as archived.
  - **Proposal: excluded from the new progress calculation.** The phase model doesn't map cleanly. A "structural / floor 3" report could be structure or walls; "MEP" isn't a stage any more. Their crew, weather, delay and problem data still count in the non-progress panels (workers per week, weather, delay causes, problems), which don't depend on the phase.
- **Project 119**, which is mid-construction:
  - With old reports excluded it would show 0%. **Proposal: an opening balance.**
    - You tell me which stages and floors are finished, and the date each finished (or "finished before X").
    - I record them as dated stage reports with finished = Yes, marked "opening balance" in the records list.
  - Its new fields get the reference defaults:
    - plot 600 m², buildable 75%
    - 1 parking + 5 apartment floors (its `total_floors` is 6)
    - 4 apartments per floor, 128 walls per floor, 4 sides
    - deconstruction and support: **you tell me** (Q14)
  - Its stage plan is copied from the default.
- **Its 6 existing floor rows:** kept and given level numbers (floor_1 → Parking 0, floor_2…6 → Floor 1…5), unless you tell me the old labels meant something else (Q15). Old reports pointing at them stay valid.

### 2.6 Tests (`dotnet test`; the dynamic-schema ones against a real Postgres)
- **Stage-plan copy:**
  - A new project gets all applicable rows.
  - Editing one project's plan leaves the default and other projects unchanged.
  - A later default edit affects only new projects.
- **Eligibility:**
  - Finished units, reopen, sub-step order, duplicate stage in one submission.
  - Floor outside the scope, side > N, inactive or non-applicable stage, project not Active or paused.
  - Two concurrent submissions.
- **Repeatable section:**
  - Min/max, required-when-visible per block, field rules per block, Arabic digits and oversized values in a block.
  - Atomic save: a failing block saves nothing.
  - Edit and soft delete.
  - The reporting view gives one row per block with the header columns.
  - Archived fields and NOT NULL handling on v1 → v2.
- **Calculations:**
  - Days-earned capping, the finished override, and one block versus separate submissions giving identical results.
  - Structure/walls overlap (walls start days 21, 31, 41, 51, 61, 70; 78).
  - Pause exclusion, and pushing planned dates forward.
  - **Reference totals 170 / 194, 158 / 369, 328 / 563 from the seed data.**
- **Overview and stakeholder report:** on the new model.

**Browser checks** (Playwright against the built site, then live staging):
- English desktop 1440 and Arabic phone 390.
- As admin and as project manager.
- Add and remove stage blocks, with visibility inside each block.
- The paused-project message, the offline queue with blocks, records and the drawer showing blocks, and the overview.

### 2.7 Order of work (each step: build → test → deploy to staging → check live)
1. Calendar service and the stage calculators, as pure code with the reference-total tests. No UI yet, so the numbers are agreed first.
2. Engine: repeatable section, multi-select, row filter, row-attribute visibility, calculated field, system and hidden flags. Builder and renderer for each.
3. Stage module: hooks, options provider, the forms (seeded on staging through the API) and pause/resume.
4. Overview and stakeholder reports rewired; old placeholders removed.
5. Project 119's opening balance on staging (a copy of production's data is not needed; staging has its own 119).
6. Full test run and browser checks, then the staging report.

Production: not touched. A later release would need the same steps as v2.0 (backup, migrations, the form versions through the API, the opening balance), and only on your word.

---

## 3. Stage names: labels I'd change

| # | As given | Suggestion | Why |
|---|---|---|---|
| 8 | Deconstruction and land cleaning | **Demolition** and site clearing | "Deconstruction" reads as careful dismantling |
| 9 | Support (3 sides) | **Shoring** (3 sides) | the engineering term for السند |
| 11 | Foundation / القواعد | Foundation / **الأساسات** | القواعد means footings only; v1 already uses الأساسات for foundation |
| 14–16 | Liasa: … | **Plastering**: … (keep لياسة in Arabic) | the English label is an Arabic loanword; mixed language |
| 17 | Gypsum and roofing / الجبس والأسقف | Gypsum **ceilings** / الجبس والأسقف **المعلقة** | it's suspended gypsum ceilings, not roofing |
| 20–22 | Kitchen and bathroom isolation | Kitchen and bathroom **waterproofing** | "isolation" is a literal translation of عزل |
| 12 | Structure (pillars and roof) / الهيكل الخرساني | keep, Arabic + "(الأعمدة والأسقف)" | so it matches the English |

Everything else reads correctly to me.

---

## 4. Questions: yours to answer (nothing is assumed)

From the task:
1. **Ahead/behind:** compare progress against the fastest, the slowest, or the midpoint of the planned range?
2. **Weekly day off:** Friday only?
3. **Pause/Resume:** administrators only, or project managers too?
4. **Editing a project's stage plan:** administrators only?
5. **Floor count reduced** after reports exist: what happens to reports on the removed floors? (My suggestion: the floor is deactivated, its reports and earned days stay visible but no longer count, and no new reports can use it.)

Found in the audit:

6. **Approach:** OK with the split in §2.1? That means generic engine pieces, plus a named server-side stage module for the plan-driven options and rules, instead of a fully generic rules language.
7. **Project codes:** OK to keep `project_code` / `project_name` / `start_date` instead of renaming them to `code` / `name` / `planned_start_date`?
8. **The 10% slack** currently added to planned dates: keep it on the new plan, or drop it?
9. **Structure and walls:** confirm they keep the fixed overlap schedule (78 days) in both the fastest and slowest totals. That's the only way the reference totals hold. Also confirm `structure` and `walls` as reserved stage codes, rather than a schedule-rule column.
10. **Location:** a new `location` field, or reuse the existing `city`?
11. **Most stage blocks** in one daily report: is 10 enough?
12. **Pause reason "Other":** add an optional note field?
13. **Same unit twice on one day** (two submissions both saying floor 3, structure, plan met): should it earn 1 day or 2? I propose 1.
14. **Project 119:** does it need demolition, and does it need shoring? Which stages and floors are already finished, and when? (This is for the opening balance.)
15. **Project 119's 6 floor rows:** is floor_1 the parking level and floor_2…6 apartment floors 1…5?
16. **Projects in Planning status:** paperwork happens before construction starts. Should reports be allowed while a project is in **Planning**, or only when Active (as the task says)? With "Active only", the paperwork stages can only be reported after the status is switched to Active.
17. **Deleting a project-plan row that has no reports yet:** allow a hard delete, or always deactivate?

---

## 5. Decisions (owner, 2026-11-10) - approved, building on `feature/stage-progress`, staging only

| # | Decision |
|---|---|
| 1 | Ahead/behind compares against the **midpoint** of the fastest and slowest planned dates |
| 2 | Weekly day off: **Friday**, configurable |
| 3 | Pause/Resume: **administrators only** |
| 4 | Editing a project's stage plan: **administrators only** |
| 5 | Reducing the floor count is **blocked** if any report exists on a floor that would be removed |
| 6 | Approach approved: generic engine pieces + one stage module; daily report = v2 of the same form |
| 7 | Keep `project_code`, `project_name`, `start_date` |
| 8 | **Keep the 10% slack**, applied as today; totals shown with and without (187/214, 174/406, 361/620) |
| 9 | Structure + walls: fixed 78-day overlap in both totals; identified by stage codes `structure`, `walls` |
| 10 | Location: **reuse `city`**, relabelled Location / الموقع |
| 11 | Up to **10** stage blocks per report; the foreman picks whichever stages were worked |
| 12 | Pause reason Other: **note required** when Other |
| 13 | Same unit twice on one date earns **1** day |
| 14 | 119: no demolition, no shoring; starting position confirmed at **day 4** (158 / 575), finished units without invented dates |
| 15 | 119's floors are `PG` (parking, level 0) then `01`-`05` (floors 1-5); existing Arabic labels kept, English added |
| 16 | Reports allowed in **Planning and Active**; blocked when On hold or Completed |
| 17 | Project plan rows are **always deactivated**, never deleted |
| - | Old placeholder plan values are replaced entirely by the stage plan; stakeholder reports move in the same change |
| - | Labels: Demolition, Shoring, Plastering, Waterproofing, Gypsum ceilings; الأساسات; "(الأعمدة والأسقف)" on Structure |

Opening position for an existing project is stored as baselines (no dates), not as invented reports.
The project dropdown shows Planning, Active and On hold projects (On hold so the form can say it is paused and refuse to send).

---

## 6. Build status (2026-11-10)

On staging (`feature/stage-progress` -> `staging`): migration `AddFormSectionsAndStageProgress` applied
(backup taken first), the seed run with `--project-119`, 119 at 158 of 575 days.

Tested: 312 backend tests (all, database tests on a real Postgres), 30 API checks and 21 browser
checks (English desktop as administrator, Arabic phone as project manager) on a restored copy of
staging, and 15 read-only browser checks on live staging. Reports and pauses were only sent on the copy.

Production release, when approved: back up, apply the migration, deploy, run the seed with
`--project-119` against production's API (it maps 119's floors PG/01-05 and records the starting
position), check.
