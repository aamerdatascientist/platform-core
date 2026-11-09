// Generates backfilled daily progress reports for project 119 (2026-01-20 .. 2026-11-01).
// Deterministic: same seeds -> same rows and Ids, so staging and production get identical data.
// Usage: node scripts/data/backfill-project-119.mjs > backfill.sql   (273 INSERTs; applied to staging 2026-11-02
// and production 2026-11-03 - see docs/PROJECT_STATUS.md. Do NOT run again on a database that has them.)
//
// Plan (client, 2026-11-02), as the dashboard's OverviewProgressPlan holds it: milestones
// 10/8/5/3, 18 per zone, 14 per footing, 17 per floor, 6 per MEP trade per floor.
// Owner's direction: the floors ran exactly to plan; all the lateness is in the stages
// before them, mostly paperwork (~3 months). A unit's late days are reports with a delay and
// the plan not met; it always gets exactly its planned number of met days, so every
// finished unit shows complete on the dashboard.
import { createHash } from "node:crypto";

const PROJECT = "f7b05c91-1bf5-4193-ac45-e981b3830036";
const FORM_VERSION = "22be464c-4ad7-4028-a63b-bc4baa0cdfcc";
const USER_QASIM = "fccbf619-1d02-4b00-9830-f827a2fc6362";
const ZONE_00 = "c42fc3ca-08f6-4b83-b0ab-3218b147d960";
const FOOTING_01 = "46035b58-e92a-4c41-9cf4-49591cc9669b";
const FOOTING_00 = "c4a0f30c-1c26-41b1-8f63-f592bd1386b2";
const FLOORS = { PG: "ad453fbf-6f5a-4dbf-a3b5-043b5b192f9e", "01": "7df57981-86e1-4e42-83df-2d3724d1d715",
                 "02": "8abd250b-1c80-42dd-91d9-496d4324554d", "03": "00cd555c-7f5b-40e3-b04c-6ab0f3301597" };
const START = "2026-01-20", END = "2026-11-01";        // END = the day before the real entry
const TAG = '{"backfill":"2026-11-02"}';

// Saudi public holidays on which the site is closed (Fridays are skipped separately).
const HOLIDAYS = new Set(["2026-02-22", "2026-03-19", "2026-03-20", "2026-03-21", "2026-03-22",
                          "2026-05-26", "2026-05-27", "2026-05-28", "2026-05-29", "2026-09-23"]);

// Separate sequences per concern, so changing one never reshuffles the others.
const stream = s => () => ((s = (s * 1103515245 + 12345) % 2147483648) / 2147483648);
const rnd = stream(20261102), wrnd = stream(7041), lrnd = stream(5309), mrnd = stream(9113);
const pick = (a, r = rnd) => a[Math.floor(r() * a.length)];
const uuid = s => { const h = createHash("sha256").update("backfill-119|" + s).digest("hex");
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-4${h.slice(13, 16)}-a${h.slice(17, 20)}-${h.slice(20, 32)}`; };

const days = [];
for (let d = new Date(START + "T00:00:00Z"); d <= new Date(END + "T00:00:00Z"); d.setUTCDate(d.getUTCDate() + 1)) {
  const iso = d.toISOString().slice(0, 10);
  if (d.getUTCDay() !== 5 && !HOLIDAYS.has(iso)) days.push(iso);
}

// ------------------------------------------------------------------ schedule
// Each unit: { phase, field, value, days (window length), planned (met days) }.
const units = [
  { phase: "paperwork", milestone: "design_approval", days: 24, planned: 10 },
  { phase: "paperwork", milestone: "permit_application", days: 44, planned: 8 },
  { phase: "paperwork", milestone: "utility_approvals", days: 27, planned: 5 },
  { phase: "paperwork", milestone: "final_permit", days: 9, planned: 3 },        // paperwork: 104 for 26 planned
  { phase: "excavation", zone: ZONE_00, days: 30, planned: 18 },
  { phase: "foundation", footing: FOOTING_01, days: 25, planned: 14 },
  { phase: "foundation", footing: FOOTING_00, days: 25, planned: 14 },
  ...["PG", "01", "02"].map(f => ({ phase: "structural", floor: FLOORS[f], floorCode: f, days: 17, planned: 17 })),
  { phase: "structural", floor: FLOORS["03"], floorCode: "03", days: 2, planned: 2 },   // started Oct 31
];
if (units.reduce((s, u) => s + u.days, 0) !== days.length)
  throw new Error(`schedule ${units.reduce((s, u) => s + u.days, 0)} != working days ${days.length}`);

// Rain is rare in Makkah: exactly these six days over the whole period (owner, 2026-11-02).
const RAIN_DAYS = new Set(["2026-01-26", "2026-02-12", "2026-03-30", "2026-04-20", "2026-10-20", "2026-10-29"]);
function weather(iso) {
  if (RAIN_DAYS.has(iso)) return "rain";
  const m = +iso.slice(5, 7), r = wrnd();
  const mix = [6, 7, 8, 9].includes(m) ? { extreme_heat: 0.3, sandstorm: 0.03, other: 0.03 }
            : m === 5 || m === 10 ? { extreme_heat: 0.12, sandstorm: 0.04, other: 0.04 }
            : m === 3 || m === 4 ? { sandstorm: 0.08, other: 0.05 }
            : { sandstorm: 0.03, other: 0.06 };                     // Jan, Feb, Nov
  let edge = 0;
  for (const [w, share] of Object.entries(mix)) if (r < (edge += share)) return w;
  return "clear";
}
const weatherOn = new Map(days.map(iso => [iso, weather(iso)]));

const CREW = { paperwork: [1, 3], excavation: [4, 6], foundation: [6, 8], structural: [5, 8], mep: [2, 4] };
const LATE_CAUSES = {
  design_approval: ["ملاحظات على المخططات من الجهة المختصة", "تأخر اعتماد المالك للتصاميم", "بانتظار اعتماد المكتب الهندسي"],
  permit_application: ["بانتظار رد الأمانة على طلب الترخيص", "نواقص في مستندات الطلب", "إعادة تقديم الطلب بعد ملاحظات البلدية"],
  utility_approvals: ["بانتظار موافقة شركة الكهرباء", "بانتظار موافقة شركة المياه", "تأخر رد جهة الخدمات"],
  final_permit: ["بانتظار إصدار الترخيص النهائي من الأمانة"],
  site: ["تأخر توريد الخرسانة الجاهزة", "تأخر وصول حديد التسليح", "عطل في المعدات", "نقص في العمالة",
         "بانتظار اعتماد الاستشاري", "تأخر فحص المختبر"],
};
const WEATHER_DELAYS = {
  extreme_heat: "توقف العمل وقت الظهيرة بسبب الحرارة الشديدة",
  rain: "هطول أمطار وتجمع مياه في الموقع",
  sandstorm: "عاصفة رملية أوقفت العمل في الموقع",
};

const q = v => v === null || v === undefined ? "NULL" : typeof v === "string" ? `'${v.replace(/'/g, "''")}'` : String(v);
const out = [];
function row(iso, idKey, phase, unit, delay, planMet) {
  const [lo, hi] = CREW[phase];
  const r = phase === "mep" ? mrnd : rnd;
  const crew = lo + Math.floor(r() * (hi - lo + 1));
  const created = new Date(`${iso}T13:00:00Z`); created.setUTCMinutes(Math.floor(r() * 240));
  const cols = {
    Id: uuid(idKey), FormVersionId: FORM_VERSION, CreatedAtUtc: created.toISOString(), CreatedByUserId: USER_QASIM,
    IsDeleted: false, Extensions: TAG, project: PROJECT, report_date: `${iso} 00:00:00+03`,
    crew_count: crew, weather: weatherOn.get(iso), phase,
    milestone: unit.milestone ?? null, zone: unit.zone ?? null, footing: unit.footing ?? null, floor: unit.floor ?? null,
    mep_trade: unit.mep_trade ?? null, mep_room: unit.mep_room ?? null,
    had_delay: !!delay, delay_cause: delay, has_problem_today: false, problem_description: null,
    plan_completed_today: planMet,
  };
  out.push(`INSERT INTO "Data_DailyProgressReport" (${Object.keys(cols).map(k => `"${k}"`).join(", ")}) VALUES (${Object.values(cols).map(q).join(", ")});`);
}

// ------------------------------------------------------------------ main reports
let i0 = 0;
const structEnd = {};
for (const u of units) {
  const window = days.slice(i0, i0 + u.days);
  // Late days: on site, bad-weather days first (that's what stopped work); the rest at random.
  const late = new Set();
  const lateCount = u.days - u.planned;
  if (u.phase !== "paperwork")
    for (const iso of window) if (late.size < lateCount && WEATHER_DELAYS[weatherOn.get(iso)] && weatherOn.get(iso) !== "extreme_heat") late.add(iso);
  const order = window.map(iso => [lrnd(), iso]).sort((a, b) => a[0] - b[0]).map(x => x[1]);
  for (const iso of order) if (late.size < lateCount) late.add(iso);
  for (const iso of window) {
    const w = weatherOn.get(iso);
    const delay = !late.has(iso) ? null
      : u.phase === "paperwork" ? pick(LATE_CAUSES[u.milestone], lrnd)
      : WEATHER_DELAYS[w] ?? pick(LATE_CAUSES.site, lrnd);
    row(iso, iso, u.phase, u, delay, !late.has(iso));
  }
  console.error(`  ${(u.phase + " " + (u.milestone ?? u.floorCode ?? (u.footing === FOOTING_00 ? "footing 00" : u.footing ? "footing 01" : "zone 00"))).padEnd(32)} ${window[0]} .. ${window[window.length - 1]}  ${u.days} days, ${u.planned} planned`);
  if (u.floorCode) structEnd[u.floorCode] = i0 + u.days - 1;
  i0 += u.days;
}

// ------------------------------------------------------------------ MEP alongside the structure
// Electrical and plumbing are finished on PG, 01 and 02 (MEP floors floor_1..floor_3): 6 days
// per trade per floor, both trades in parallel, ending 4 working days after that floor's
// structure (or today, whichever is first). On plan, so every day met.
for (const [floorCode, room] of [["PG", "floor_1"], ["01", "floor_2"], ["02", "floor_3"]]) {
  const endIdx = Math.min(structEnd[floorCode] + 4, days.length - 1);
  for (let i = endIdx - 5; i <= endIdx; i++)
    for (const trade of ["electrical", "plumbing"])
      row(days[i], `${days[i]}|${trade}`, "mep", { mep_trade: trade, mep_room: room }, null, true);
  console.error(`  ${("mep " + room + " (" + floorCode + ")").padEnd(32)} ${days[endIdx - 5]} .. ${days[endIdx]}  6 days x 2 trades`);
}
console.error(`rows ${out.length}`);
console.log(out.join("\n"));
