# Stakeholder reports: custom from/to dates (2026-11-28)

**Status:** live on **production** (`main` = `7fec680`) and **staging** (`staging` = `984b9f6`, a merge
of the same commit). Released on the owner's request: "choose custom dates for the stakeholder report,
from date till to date, in staging and production". Only this change went to production; none of
staging's held-back work did. No migration, no settings, no data change.

## What changed for the administrator

Stakeholder reports → **Make a report now** has a fourth choice next to Daily / Weekly / Monthly:
**Custom** (Arabic **مخصص**). It shows **From** and **To** in place of "Up to". From starts at the 1st
of the current month and To at today.

- The report covers exactly the picked days and is compared with **the same number of days just
  before them**. For example, 1-28 November is compared with 4-31 October.
- Charts: a range of up to two weeks is drawn **day by day**, a longer one **week by week**. There is
  no 8-week / 6-month trend chart, because a picked range has no fixed rhythm.
- PDF wording: title **"تقرير فترة مختارة"**, period **"من 1 نوفمبر 2026 إلى 28 نوفمبر 2026"** (a
  single day shows just that day), comparisons **"عن الفترة السابقة"**, "this period" = **"هذه الفترة"**.
  The PDF file name is `Asas-report-{from}-to-{to}.pdf`.
- **Preview without sending** works as before: it makes the PDFs and sends nothing.
- **Send now** goes to **every active recipient who receives any regular report** (daily, weekly or
  monthly). A recipient has no "custom" checkbox, so this is the rule. The confirmation names the
  dates: "Send the report for … to … to every report recipient on WhatsApp now?".
- Limits: both days are required, From ≤ To, at most **366 days**, and To can't be in the future.
- Recent reports shows a custom run as "Custom dates · Preview" (or Send now) with its date range
  underneath.
- **Daily, weekly and monthly reports and their schedules are unchanged.** Custom is never scheduled.

## API

`POST /api/reports/run` (administrators, same rate limit of 10 per hour) takes an optional `from`:

```json
{ "kind": "custom", "from": "2026-11-01", "to": "2026-11-28", "withAnalysis": true, "deliver": false }
```

- `kind` may now be `daily`, `weekly`, `monthly` or **`custom`**.
- For `custom`, `from` and `to` are required (400 with a clear message otherwise: missing day,
  from after to, more than 366 days, to in the future).
- The other kinds refuse `from` (400 "Only a custom report takes a first day"). Their behaviour,
  including `to` defaulting to today, is unchanged.
- The returned `ReportRunDto.kind` can be `"custom"`, with `periodStart` / `periodEnd` = the picked days.

## Code

| Where | What |
|---|---|
| `Platform.Domain/Reports/ReportRun.cs` | `Custom = "custom"`, `ManualKinds` (the three + custom), `MaxCustomDays = 366`. `Kinds` (scheduled / recipients) is unchanged. |
| `Platform.Domain/Reports/ReportRecipient.cs` | `Receives(Custom)` = receives daily, weekly or monthly (and is active). |
| `Platform.Application/Reports/StakeholderReportBuilder.cs` | `Period(kind, to, from?)`: custom = the picked days + the same number of days before. `Build(..., customFrom?)`. `SeriesByDay`: weekly, or custom up to 14 days. |
| `Platform.Application/Reports/StakeholderReportService.cs` | `RunAsync(..., customFrom?)`; `ReportTexts` title, period and file name for custom. |
| `Platform.Application/Reports/Commands/RunReport/RunReportCommand.cs` | `From` + the validation rules above. |
| `Platform.Api/Controllers/ReportsController.cs` | `RunRequest.From` (optional). |
| `Platform.Infrastructure/Reports/ReportHtmlRenderer.cs` | Custom wording (`PreviousWord`, `PeriodWord`, chart titles, "what to watch"). |
| `Platform.Infrastructure/Reports/ClaudeReportAnalyst.cs` | One line in the system prompt explaining the `custom` report type. |
| `platform-web/src/routes/StakeholderReports.tsx` | Custom choice, From/To pickers, confirmation text, range in Recent reports. |
| `platform-web/src/types.ts`, `api/client.ts`, `i18n/locales/{en,ar}.json` | `RunKind`, `from`, new strings (en/ar parity 941 keys on `main`, 1051 on staging). |

The scheduler (`StakeholderReportScheduler`) is untouched. The optional parameters default to the old
behaviour, so scheduled daily/weekly/monthly runs work exactly as before.

## How it was tested

- **Automated:** 8 new tests in `StakeholderReportTests`:
  - the custom period and its comparison period, and a start missing or after the end;
  - every validator rule, plus "regular kinds unchanged";
  - which recipients get a custom report;
  - a custom send-now run that stores the range and sends to the right people with the Arabic title
    and period;
  - the PDF page for a 6-day range (day by day) and a 60-day range (week by week).

  All other tests pass. The only failures are the Docker-only database tests, which can't run on the
  Mac: 18 on `main` and 24 on staging.
- **Local, real data:**
  - the API was run against a local copy of the 2026-11-06 staging backup, with a local stand-in for
    Gotenberg (Chromium printing the page);
  - custom ranges of 15 Oct-5 Nov (charted by week) and 20-28 Oct (charted by day) produced correct
    A4 and phone PDFs;
  - a weekly report was unchanged ("الأسبوع 31 أكتوبر - 5 نوفمبر 2026").
- **Browser:** 7/7 checks on `main` and on the merged staging code, covering:
  - English laptop and Arabic phone;
  - the request sent;
  - the range in Recent reports;
  - the send confirmation;
  - the page fitting a 390 px phone.

  One fix came out of this: the selector says "Custom" / "مخصص", because "فترة مختارة" was cut off on
  a phone.
- **After release:**
  - deploys green on both;
  - the new strings are in both live bundles;
  - production answers 401 to a signed-out `POST /api/reports/run`.
- **Owner on staging, 2026-11-28 16:52:** Custom, 1-28 November, Send now, with analysis. Result: 200,
  status `done`, analysis `written`, **$0.098** of the $15 cap, PDF made, 47 seconds.

## Known problem: staging returns 500 to a signed-out "make a report" (open)

**What:** on **staging only**, `POST /api/reports/run` **without signing in** answers **500** ("An
unexpected error occurred") instead of **401**. It was found right after the 2026-11-28 staging deploy.

**What it is not:**
- **Not this change.** Production runs the same report code and answers 401. The merged staging code,
  run locally, also answers 401.
- **Not a problem for signed-in use.** The owner's signed-in custom report on staging worked (above).
- **Not other endpoints.** Every other signed-out staging endpoint checked answered normally: GET
  `/api/reports`, POST `/api/reports/recipients`, POST `/api/form-assignments/remind-pending` and the
  report download all gave 401, and sign-in gave 400.

**Likely area (not confirmed):** something only staging has, from 2026-11-23/24:
- the Veronica service credential (`Security/VeronicaCredential.cs`, #33), which hooks into JWT
  sign-in;
- the health probes and rate-limit change (#34, `RateLimitPolicies.HealthDependencies`);
- or a staging-only App Service setting.

`POST /api/reports/run` is the only endpoint with the `reports` rate-limit policy, and that policy is
where it differs from the others. The middleware order (`UseRateLimiter` → `UseAuthentication` →
`UseAuthorization`) is the same as production's.

**Why it wasn't fixed:** the exception is only visible in staging's App Service log stream, which
can't be reached from the owner's Mac session.

**Next step:** open `asas-api-staging` → **Log stream** (or App Service logs), send
`curl -X POST -H "Content-Type: application/json" -d "{}" https://asas-api-staging-ancbhsa7grf5emfh.eastasia-01.azurewebsites.net/api/reports/run`,
and read the exception logged by `ExceptionHandlingMiddleware`. Low priority while signed-in use works.
