#!/usr/bin/env python3
"""
Generates the dummy data set for the seven live forms: 5 clearly-marked dummy projects
(codes DUMMY-01..05) with their zones/footings/floors, three months of daily progress
reports, and matching stock inflow/outflow.

Input : the live structure exported by scripts/export-live-structure.ps1
        (Claude outputs/live-structure.json) - every field code, type, option value and
        visibility condition used below is checked against it, so the output can only
        ever contain what the live forms accept today.
Output: dummy-rows.tsv        one row per line: key <TAB> formCode <TAB> JSON body
        expected-structure.txt one signature line per live field, re-checked by the
                               loader against the live API before it writes anything.

Deterministic (fixed random seed) - re-running produces the identical data set.

Usage: python generate_dummy_data.py <live-structure.json> <output-dir>
"""
import json, random, sys, datetime as dt
from collections import defaultdict, OrderedDict

SEED = 20261010
WINDOW_START = dt.date(2026, 7, 11)
WINDOW_END = dt.date(2026, 10, 10)

src, outdir = sys.argv[1], sys.argv[2]
live = json.load(open(src, encoding="utf-8-sig"))
FORMS = {f["definition"]["code"]: f["definition"] for f in live["forms"]}
ID2CODE = {d["id"]: c for c, d in FORMS.items()}
rng = random.Random(SEED)


# ---------------------------------------------------------------- live structure helpers
def active_fields(form_code):
    return [f for f in FORMS[form_code]["publishedVersion"]["fields"] if f["isActive"]]

def option_values(field):
    return [o["value"] for o in json.loads(field["optionsJson"])] if field["optionsJson"] else []

def visible_values(field):
    """VisibleWhenValuesJson as a list. Some live conditions are stored as a bare JSON
    string ("true") instead of a one-item array - both shapes are read the way the form
    itself (FormRenderer) treats them."""
    if not field["visibleWhenFieldCode"]:
        return None
    v = json.loads(field["visibleWhenValuesJson"])
    return v if isinstance(v, list) else [v]

def signature(form_code, f):
    target = ID2CODE.get(f["lookupFormDefinitionId"], "") if f["lookupFormDefinitionId"] else ""
    dyn = ""
    if f["dynamicOptionsSourceFormDefinitionId"]:
        dyn = ID2CODE.get(f["dynamicOptionsSourceFormDefinitionId"], "?") + "." + (f["dynamicOptionsSourceFieldCode"] or "")
    vis = ""
    if f["visibleWhenFieldCode"]:
        vis = f["visibleWhenFieldCode"] + "=" + ",".join(visible_values(f))
    return "|".join([form_code, f["code"], f["fieldType"], "req" if f["isRequired"] else "opt",
                     ",".join(option_values(f)), target, f["filterByFieldCode"] or "", vis, dyn])


# ---------------------------------------------------------------- row collection + validation
ROWS = []          # (key, form_code, values)
KEYS = set()

def ref(key):
    assert key in KEYS, f"reference to a row that does not exist yet: {key}"
    return "@@ref:" + key + "@@"

def add_row(key, form_code, values):
    assert key not in KEYS, key
    validate(form_code, values)
    KEYS.add(key)
    ROWS.append((key, form_code, values))

def validate(form_code, values):
    """Stricter than the backend's SubmissionValueValidator: a conditional field must be
    present exactly when its condition holds, and never otherwise."""
    fields = {f["code"]: f for f in active_fields(form_code)}
    unknown = set(values) - set(fields)
    assert not unknown, f"{form_code}: unknown field(s) {unknown}"
    for code, f in fields.items():
        if f["fieldType"] == "Attachment":
            assert code not in values
            continue
        vis = visible_values(f)
        shown = True
        if vis is not None:
            ctrl = values.get(f["visibleWhenFieldCode"])
            ctrl_s = ("true" if ctrl else "false") if isinstance(ctrl, bool) else ctrl
            shown = ctrl_s in vis
        present = code in values and values[code] is not None
        if not shown:
            assert not present, f"{form_code}.{code} sent while hidden"
            continue
        # a conditional field counts as wanted whenever it is shown, required or not
        if f["isRequired"] or vis is not None:
            assert present, f"{form_code}.{code} missing"
        if not present:
            continue
        v, t = values[code], f["fieldType"]
        if t == "Number":
            assert isinstance(v, int) and not isinstance(v, bool), (form_code, code, v)
        elif t == "Decimal":
            assert isinstance(v, (int, float)) and not isinstance(v, bool) and v > 0
        elif t == "Boolean":
            assert isinstance(v, bool)
        elif t == "DateTime":
            dt.date.fromisoformat(v)
        elif t == "Dropdown":
            assert isinstance(v, str) and v
            if not f["dynamicOptionsSourceFormDefinitionId"]:
                assert v in option_values(f), f"{form_code}.{code}: '{v}' is not a live option"
        elif t == "Lookup":
            assert v.startswith("@@ref:" + ID2CODE[f["lookupFormDefinitionId"]] + "|"), (form_code, code, v)
        elif t in ("ShortText", "LongText"):
            assert isinstance(v, str) and v.strip() and "\t" not in v and "\n" not in v


# ---------------------------------------------------------------- the five dummy projects
# Every project starts inside the window, so each one's whole history is in the data and
# nothing has to be assumed about work done before the first report.
PROJECTS = [
    dict(code="DUMMY-01", name="فلل الندى السكنية (تجريبي)", client="شركة الندى للتطوير العقاري (تجريبي)",
         city="الرياض", type="residential", status="completed", manager="م. خالد العتيبي",
         floors=2, zones=3, footings=4, start=dt.date(2026, 7, 11), expected=dt.date(2026, 10, 15),
         pace=0.90, crews=1, size=1.0, stop=None,
         stores=["مستودع موقع فلل الندى"],
         notes="بيانات تجريبية - مشروع صغير أنجز جميع المراحل"),
    dict(code="DUMMY-02", name="برج الياسمين السكني (تجريبي)", client="مجموعة الياسمين العقارية (تجريبي)",
         city="جدة", type="residential", status="active", manager="م. سارة الغامدي",
         floors=4, zones=4, footings=6, start=dt.date(2026, 7, 11), expected=dt.date(2026, 11, 30),
         pace=0.84, crews=1, size=1.4, stop=None,
         stores=["مستودع موقع برج الياسمين", "المستودع المركزي - جدة"],
         notes="بيانات تجريبية - مشروع يسير حسب الخطة"),
    dict(code="DUMMY-03", name="مجمع الريان التجاري (تجريبي)", client="شركة الريان للاستثمار (تجريبي)",
         city="الرياض", type="commercial", status="active", manager="م. فهد الدوسري",
         floors=5, zones=6, footings=10, start=dt.date(2026, 7, 18), expected=dt.date(2026, 12, 15),
         pace=0.58, crews=2, size=1.8, stop=None,
         stores=["مستودع موقع مجمع الريان", "ساحة التخزين الخارجية - الريان"],
         notes="بيانات تجريبية - مشروع متأخر عن الخطة"),
    dict(code="DUMMY-04", name="مستودعات الخليج الصناعية (تجريبي)", client="شركة الخليج للخدمات اللوجستية (تجريبي)",
         city="الدمام", type="industrial", status="on_hold", manager="م. عبدالله القحطاني",
         floors=1, zones=8, footings=12, start=dt.date(2026, 8, 1), expected=dt.date(2026, 12, 31),
         pace=0.78, crews=2, size=1.6, stop=dt.date(2026, 9, 15),
         stores=["مستودع موقع مستودعات الخليج"],
         notes="بيانات تجريبية - مشروع متوقف مؤقتاً منذ منتصف سبتمبر"),
    dict(code="DUMMY-05", name="مدرسة الفيصل الأهلية (تجريبي)", client="مؤسسة الفيصل التعليمية (تجريبي)",
         city="المدينة المنورة", type="other", status="active", manager="م. نورة الحربي",
         floors=3, zones=5, footings=8, start=dt.date(2026, 9, 5), expected=dt.date(2027, 3, 31),
         pace=0.82, crews=1, size=1.2, stop=None,
         stores=["مستودع موقع مدرسة الفيصل"],
         notes="بيانات تجريبية - مشروع في مراحله الأولى"),
]

# planned working days ("yes" days needed) per unit, before the project's own size factor
MILESTONE_DAYS = OrderedDict([("design_approval", 4), ("permit_application", 3), ("utility_approvals", 2), ("final_permit", 1)])
ZONE_DAYS, FOOTING_DAYS, FLOOR_DAYS, MEP_DAYS = 2, 2, 8, 3
MEP_TRADES = ["electrical", "plumbing", "hvac"]

DELAY_CAUSES = {
    "extreme_heat": ["حظر العمل تحت أشعة الشمس وقت الظهيرة", "توقف العمل بسبب الحرارة الشديدة"],
    "sandstorm": ["عاصفة رملية أوقفت العمل في الموقع", "انعدام الرؤية بسبب الغبار"],
    "rain": ["هطول أمطار وتجمع مياه في الموقع", "توقف الصب بسبب الأمطار"],
    None: ["تأخر توريد الخرسانة الجاهزة", "تأخر وصول حديد التسليح", "عطل في الحفار", "عطل في مضخة الخرسانة",
           "نقص في العمالة", "بانتظار اعتماد الاستشاري", "تأخر وصول الرافعة", "انقطاع التيار الكهربائي في الموقع",
           "تعارض مع خطوط خدمات قائمة", "تأخر فحص المختبر"],
}
DELAY_CAUSES_PAPERWORK = ["بانتظار رد الأمانة", "ملاحظات على المخططات من الجهة المختصة", "تأخر اعتماد المالك",
                          "نواقص في مستندات الطلب"]
PROBLEMS = {
    "paperwork": ["طلب الجهة المختصة تعديلات على المخططات المعمارية.", "نقص في المستندات المطلوبة لاستكمال الطلب."],
    "excavation": ["ظهور مياه جوفية في قاع الحفر وتحتاج إلى نزح.", "وجود صخور صلبة تتطلب معدات تكسير إضافية.",
                   "انهيار جزئي في جوانب الحفر ويحتاج إلى تدعيم."],
    "foundation": ["ملاحظات من الاستشاري على تسليح القاعدة قبل الصب.", "نتيجة اختبار الهبوط للخرسانة خارج الحدود المسموحة.",
                   "تسرب في الشدات الخشبية أثناء الصب."],
    "structural": ["تعشيش في خرسانة أحد الأعمدة بعد فك الشدات.", "تأخر في تسليم مخططات الورشة المعتمدة.",
                   "إصابة عمل بسيطة وتم التعامل معها في الموقع.", "انحراف في منسوب الشدة ويحتاج إلى إعادة ضبط."],
    "mep": ["تعارض بين مسارات التكييف والكهرباء في السقف.", "مواد موردة غير مطابقة للمواصفات المعتمدة.",
            "تسرب في اختبار ضغط شبكة المياه."],
}

def is_working_day(d):
    return d.weekday() != 4          # Friday off

def working_days(start, end):
    d = start
    while d <= end:
        if is_working_day(d):
            yield d
        d += dt.timedelta(days=1)

_weather_cache = {}
def weather_for(city, day):
    """One weather value per city per day, shared by every report from that city."""
    key = (city, day)
    if key in _weather_cache:
        return _weather_cache[key]
    m = day.month
    heat = {7: 0.34, 8: 0.36, 9: 0.18, 10: 0.04}[m]
    if city in ("جدة",):
        heat *= 0.8
    rain = {7: 0.0, 8: 0.01, 9: 0.01, 10: 0.04}[m] * (2.0 if city in ("جدة", "المدينة المنورة") else 1.0)
    sand = {7: 0.05, 8: 0.04, 9: 0.03, 10: 0.02}[m] * (1.4 if city in ("الرياض", "الدمام") else 0.7)
    r = rng.random()
    if r < heat: w = "extreme_heat"
    elif r < heat + sand: w = "sandstorm"
    elif r < heat + sand + rain: w = "rain"
    elif r < heat + sand + rain + 0.01: w = "other"
    else: w = "clear"
    _weather_cache[key] = w
    return w


# ---------------------------------------------------------------- master data
for p in PROJECTS:
    add_row("projects|" + p["code"], "projects", dict(
        project_code=p["code"], project_name=p["name"], client_name=p["client"], city=p["city"],
        project_type=p["type"], status=p["status"], project_manager=p["manager"], total_floors=p["floors"],
        start_date=p["start"].isoformat(), expected_completion=p["expected"].isoformat(), notes=p["notes"]))

for p in PROJECTS:
    pref = ref("projects|" + p["code"])
    for i in range(1, p["zones"] + 1):
        add_row(f"project-zones|{p['code']}/zone_{i}", "project-zones",
                dict(project=pref, zone_code=f"zone_{i}", zone_label=f"المنطقة {i}"))
    for i in range(1, p["footings"] + 1):
        add_row(f"project-footings|{p['code']}/footing_{i}", "project-footings",
                dict(project=pref, footing_code=f"footing_{i}", footing_label=f"القاعدة {i}"))
    for i in range(1, p["floors"] + 1):
        add_row(f"project-floors|{p['code']}/floor_{i}", "project-floors",
                dict(project=pref, floor_code=f"floor_{i}", floor_label=f"الطابق {i}"))


# ---------------------------------------------------------------- daily progress simulation
class Unit:
    def __init__(self, phase, sel, planned):
        self.phase, self.sel, self.planned = phase, sel, max(1, round(planned))
        self.yes = 0
    @property
    def done(self):
        return self.yes >= self.planned

def crew_size(phase, size):
    base = {"paperwork": 3, "excavation": 9, "foundation": 14, "structural": 22, "mep": 8}[phase]
    return base if phase == "paperwork" else base * size

REPORTS = []                 # daily progress rows before keys are assigned
PHASE_BY_DAY = defaultdict(set)   # (project code, day) -> phases reported that day (drives stock)

def report(p, day, unit):
    weather = weather_for(p["city"], day)
    office = unit.phase == "paperwork"
    chance = p["pace"]
    weather_hit = False
    if not office:
        if weather == "extreme_heat" and rng.random() < 0.45:
            chance -= 0.30; weather_hit = True
        elif weather == "sandstorm" and rng.random() < 0.85:
            chance -= 0.65; weather_hit = True
        elif weather == "rain" and rng.random() < 0.75:
            chance -= 0.45; weather_hit = True
    other_delay = rng.random() < (0.10 + (0.85 - p["pace"]) * 0.6)
    had_delay = weather_hit or other_delay
    if other_delay:
        chance -= 0.35
    completed = rng.random() < max(0.05, chance)
    crew = crew_size(unit.phase, p["size"]) * rng.uniform(0.85, 1.15)
    if weather_hit:
        crew *= 0.8
    v = dict(report_date=day.isoformat(), project=ref("projects|" + p["code"]),
             crew_count=max(2, int(round(crew))), weather=weather, had_delay=had_delay, phase=unit.phase)
    if had_delay:
        if office:
            v["delay_cause"] = rng.choice(DELAY_CAUSES_PAPERWORK)
        else:
            v["delay_cause"] = rng.choice(DELAY_CAUSES[weather] if weather_hit else DELAY_CAUSES[None])
    v.update(unit.sel)
    has_problem = rng.random() < (0.07 if completed else 0.28)
    v["has_problem_today"] = has_problem
    if has_problem:
        v["problem_description"] = rng.choice(PROBLEMS[unit.phase])
    v["plan_completed_today"] = completed
    if completed:
        unit.yes += 1
    REPORTS.append((day, p["code"], v))
    PHASE_BY_DAY[(p["code"], day)].add(unit.phase)

def run_project(p):
    code, size = p["code"], p["size"]
    zref = lambda i: ref(f"project-zones|{code}/zone_{i}")
    gref = lambda i: ref(f"project-footings|{code}/footing_{i}")
    fref = lambda i: ref(f"project-floors|{code}/floor_{i}")
    # the main front: paperwork -> excavation -> foundation -> structural, strictly in order
    main = [Unit("paperwork", dict(milestone=m), d * (1 + (size - 1) * 0.5)) for m, d in MILESTONE_DAYS.items()]
    main += [Unit("excavation", dict(zone=zref(i)), ZONE_DAYS) for i in range(1, p["zones"] + 1)]
    main += [Unit("foundation", dict(footing=gref(i)), FOOTING_DAYS) for i in range(1, p["footings"] + 1)]
    floors = [Unit("structural", dict(floor=fref(i)), FLOOR_DAYS * (0.8 + size * 0.2)) for i in range(1, p["floors"] + 1)]
    main += floors
    # the MEP front runs alongside structural: a floor's three trades open once that floor's
    # structure is finished, and one MEP crew works through them in order
    mep = [(fl, Unit("mep", dict(mep_trade=t, mep_room=f"floor_{i}"), MEP_DAYS))
           for i, fl in enumerate(floors, start=1) for t in MEP_TRADES]
    last = min(p["stop"] or WINDOW_END, WINDOW_END)
    for day in working_days(p["start"], last):
        open_main = [u for u in main if not u.done]
        open_mep = [m for (fl, m) in mep if fl.done and not m.done]
        if not open_main and not open_mep:
            if all(m.done for _, m in mep):
                break
            continue
        if open_main:
            phase = open_main[0].phase
            # zones and footings can be worked by two crews at once on the bigger sites
            n = p["crews"] if phase in ("excavation", "foundation") else 1
            for u in [u for u in open_main if u.phase == phase][:n]:
                report(p, day, u)
        if open_mep:
            report(p, day, open_mep[0])
    return main, mep

SUMMARY = {}
for p in PROJECTS:
    main, mep = run_project(p)
    allu = main + [m for _, m in mep]
    SUMMARY[p["code"]] = {ph: f"{sum(u.done for u in allu if u.phase == ph)}/{sum(1 for u in allu if u.phase == ph)}"
                          for ph in ["paperwork", "excavation", "foundation", "structural", "mep"]}

REPORTS.sort(key=lambda r: (r[0], r[1]))
seq = defaultdict(int)
for day, code, v in REPORTS:
    seq[(code, day)] += 1
    add_row(f"daily-progress-report|{code}/{day.isoformat()}/{seq[(code, day)]}", "daily-progress-report", v)


# ---------------------------------------------------------------- stock inflow / outflow
# material -> (unit, typical delivery quantity range, decimals)
MATERIALS = {
    "rebar": ("ton", (8, 30), 1), "cement": ("bag", (150, 600), 0), "blocks": ("piece", (2000, 8000), 0),
    "ready_mix": ("cubic_meter", (40, 160), 1), "sand": ("cubic_meter", (20, 80), 1),
    "gravel": ("cubic_meter", (20, 80), 1), "wood": ("piece", (100, 400), 0), "pipes": ("piece", (80, 300), 0),
}
OTHER_MATERIALS = {   # sent as material="other" + material_other
    "كابلات كهربائية": ("roll", (10, 40), 0), "دهانات": ("liter", (100, 400), 0),
    "مواد عزل مائي": ("roll", (20, 60), 0), "مسامير وبراغي": ("box", (10, 40), 0),
}
# (material, relative weight) - the bulk structural materials arrive far more often than
# sundries or the free-text "other" items
PHASE_MATERIALS = {
    "excavation": [("sand", 4), ("gravel", 4), ("wood", 2)],
    "foundation": [("rebar", 5), ("ready_mix", 5), ("cement", 4), ("gravel", 2), ("wood", 2), ("مواد عزل مائي", 1)],
    "structural": [("rebar", 5), ("ready_mix", 5), ("cement", 4), ("blocks", 4), ("sand", 2), ("wood", 2), ("مسامير وبراغي", 1)],
    "mep": [("pipes", 5), ("كابلات كهربائية", 3), ("دهانات", 2)],
}

def pick_materials(phase, k):
    pool, chosen = list(PHASE_MATERIALS[phase]), []
    for _ in range(min(k, len(pool))):
        m = rng.choices(pool, weights=[w for _, w in pool])[0]
        chosen.append(m[0]); pool.remove(m)
    return chosen

REASONS_OTHER = ["عينة لفحص المختبر", "إعارة لمقاول من الباطن", "استخدام في أعمال مؤقتة بالموقع"]
SUPPLIER_NOTES = ["توريد حسب أمر الشراء.", "تم الاستلام والمطابقة مع الفاتورة.", "توريد جزئي - الباقي في الدفعة القادمة.",
                  "تم الفحص الظاهري عند الاستلام."]
reason_field = next(f for f in active_fields("stock-outflow") if f["code"] == "reason")
assert set(option_values(reason_field)) >= {"used_in_construction", "transferred", "returned_to_supplier", "damaged_or_wasted", "other"}

INFLOWS, OUTFLOWS = [], []
for p in PROJECTS:
    code, size = p["code"], p["size"]
    balance = defaultdict(float)          # (material key, store) -> quantity on hand
    since_delivery = defaultdict(lambda: 99)
    last = min(p["stop"] or WINDOW_END, WINDOW_END)
    for day in working_days(p["start"], last):
        phases = [ph for ph in ("excavation", "foundation", "structural", "mep") if ph in PHASE_BY_DAY.get((code, day), ())]
        if not phases:
            continue
        for ph in phases:
            since_delivery[ph] += 1
            # a delivery every few working days while the phase is running
            if since_delivery[ph] >= rng.randint(3, 6):
                since_delivery[ph] = 0
                for mat in pick_materials(ph, rng.randint(1, 2)):
                    unit, (lo, hi), dec = MATERIALS.get(mat) or OTHER_MATERIALS[mat]
                    qty = round(rng.uniform(lo, hi) * (0.6 + size * 0.4), dec)
                    qty = int(qty) if dec == 0 else qty
                    store = rng.choice(p["stores"])
                    v = dict(date=day.isoformat(), project=ref("projects|" + code),
                             material=mat if mat in MATERIALS else "other", quantity=qty, unit=unit, storage_location=store)
                    if mat not in MATERIALS:
                        v["material_other"] = mat
                    if rng.random() < 0.35:
                        v["notes"] = rng.choice(SUPPLIER_NOTES)
                    INFLOWS.append((day, code, v))
                    balance[(mat, store)] += qty
            # issue stock on most working days. Only the fixed-list materials are issued:
            # Outflow's material dropdown offers Inflow's stored values, so an "other"
            # material could only ever be issued as the literal value "other".
            if rng.random() < 0.6:
                on_hand = [(k, q) for k, q in balance.items() if q > 0 and k[0] in MATERIALS and k[0] in [m for m, _ in PHASE_MATERIALS[ph]]]
                if on_hand:
                    (mat, store), q = rng.choice(on_hand)
                    unit, _, dec = MATERIALS[mat]
                    take = round(q * rng.uniform(0.15, 0.45), dec)
                    take = int(take) if dec == 0 else take
                    if take > 0:
                        r = rng.random()
                        reason = ("used_in_construction" if r < 0.86 else "damaged_or_wasted" if r < 0.91
                                  else "transferred" if r < 0.95 else "returned_to_supplier" if r < 0.97 else "other")
                        v = dict(date=day.isoformat(), project=ref("projects|" + code), material=mat,
                                 quantity=take, unit=unit, issued_from=store, reason=reason)
                        if reason == "other":
                            v["reason_other"] = rng.choice(REASONS_OTHER)
                        OUTFLOWS.append((day, code, v))
                        balance[(mat, store)] -= take
    assert all(q >= -1e-6 for q in balance.values()), "an outflow exceeded stock on hand"

for name, bucket in (("stock-inflow", INFLOWS), ("stock-outflow", OUTFLOWS)):
    bucket.sort(key=lambda r: (r[0], r[1]))
    seq = defaultdict(int)
    for day, code, v in bucket:
        seq[(code, day)] += 1
        add_row(f"{name}|{code}/{day.isoformat()}/{seq[(code, day)]}", name, v)

# every Outflow value sourced from Inflow must really exist on Inflow
inflow_vals = {k: {v[k] for _, _, v in INFLOWS} for k in ("material", "unit", "storage_location")}
for _, _, v in OUTFLOWS:
    assert v["material"] in inflow_vals["material"] and v["unit"] in inflow_vals["unit"] and v["issued_from"] in inflow_vals["storage_location"]


# ---------------------------------------------------------------- write
with open(outdir + "/dummy-rows.tsv", "w", encoding="utf-8", newline="\n") as fh:
    for key, form_code, values in ROWS:
        body = json.dumps(values, ensure_ascii=False, separators=(",", ":"))
        assert "\t" not in body and "\n" not in body
        fh.write(f"{key}\t{form_code}\t{body}\n")

with open(outdir + "/expected-structure.txt", "w", encoding="utf-8", newline="\n") as fh:
    for form_code in sorted(FORMS):
        for f in sorted(active_fields(form_code), key=lambda x: x["code"]):
            fh.write(signature(form_code, f) + "\n")

counts = defaultdict(int)
for _, fc, _ in ROWS:
    counts[fc] += 1
print("rows per form:", dict(counts), "total", len(ROWS))
print("units finished per project (done/total):")
for c, s in SUMMARY.items():
    print("  ", c, s)
