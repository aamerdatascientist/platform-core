using System.Globalization;
using System.Text;
using Platform.Application.Reports;
using Platform.Domain.Reports;

namespace Platform.Infrastructure.Reports;

/// <summary>
/// The stakeholder report as one HTML page (design "D", chosen by the owner 2026-11-06): the
/// dashboard's figures and charts as evidence, wrapped in the written analysis - summary, key
/// findings, a diagnosis per project, recommendations, what to watch. The same page prints as
/// A4 or as one long phone-width page; <c>body.phone</c> switches the layout to one column and
/// turns tables into cards. Arabic, right to left; charts run left to right like the dashboard.
/// </summary>
public class ReportHtmlRenderer : IReportRenderer
{
    private const string Ink = "#161A1E", Soft = "#5C6670", Line = "#D5D9DD", Bg = "#EEF0F2", Accent = "#3A7CB0",
        Accent2 = "#C98826", Good = "#2F7D50", Warn = "#C98826", Bad = "#B14A3B", Navy = "#14202B";

    public string RenderHtml(StakeholderReport r, ReportAnalysis? analysis, string? analysisNote, bool phone)
    {
        var html = new StringBuilder();
        // No scripts, no network: even if escaping ever failed, nothing in the page could run or
        // fetch anything while the PDF service prints it. Only the bundled fonts (files sent with it).
        html.Append("<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\">")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src file: 'self'; img-src data:\">")
            .Append("<style>")
            .Append(Css).Append("</style></head><body class=\"").Append(phone ? "phone" : "a4").Append("\"><div class=\"wrap\">");

        Hero(html, r);
        if (analysisNote is not null) html.Append("<div class=\"notice\">").Append(E(analysisNote)).Append("</div>");

        html.Append("<div class=\"summary\"><h3>الخلاصة</h3>").Append(E(analysis?.Summary ?? PlainSummary(r))).Append("</div>");

        if (analysis is { Findings.Count: > 0 })
        {
            html.Append("<h2>أبرز النتائج</h2><div class=\"fds\">");
            var n = 1;
            foreach (var f in analysis.Findings)
                html.Append("<div class=\"fd\"><div class=\"fdn\">").Append(n++).Append("</div><div class=\"fdb\" style=\"color:")
                    .Append(ToneColor(f.Tone, Ink)).Append("\">").Append(E(f.Headline)).Append("</div><div><b>").Append(E(f.Title))
                    .Append("</b><p>").Append(E(f.Text)).Append("</p></div></div>");
            html.Append("</div>");
        }

        Charts(html, r, analysis);

        html.Append("<div class=\"pb\"></div><h2>تحليل المشاريع</h2>");
        foreach (var p in r.Projects) Project(html, r, p, analysis?.Projects.FirstOrDefault(a => a.Code == p.Code));

        html.Append("<div class=\"grid2\"><div class=\"card\"><h3>أكثر أسباب التأخير · ")
            .Append(r.IsDaily ? "آخر 30 يوماً" : PeriodWord(r)).Append("</h3>");
        if (r.DelayCauses.Count == 0) html.Append("<p class=\"sub\">لم تُسجل أسباب تأخير.</p>");
        else HBars(html, r.DelayCauses);
        Note(html, analysis?.ChartNotes.Causes);
        html.Append("</div><div class=\"card\"><h3>").Append(r.IsDaily ? "يحتاج انتباه اليوم" : "المشاكل المسجلة").Append("</h3>");
        Issues(html, r);
        html.Append("</div></div>");

        if (analysis is { Recommendations.Count: > 0 })
        {
            html.Append("<h2>التوصيات</h2><table class=\"hide-phone\"><tr><th>الأولوية</th><th>المشروع</th><th>الإجراء</th><th>المسؤول</th><th>الموعد</th></tr>");
            foreach (var rec in analysis.Recommendations)
                html.Append("<tr><td>").Append(Priority(rec.Priority)).Append("</td><td class=\"num\"><b>").Append(E(rec.ProjectCode))
                    .Append("</b></td><td>").Append(E(rec.Action)).Append("</td><td>").Append(E(rec.Owner)).Append("</td><td>")
                    .Append(E(rec.Due)).Append("</td></tr>");
            html.Append("</table><div class=\"show-phone\">");
            foreach (var rec in analysis.Recommendations)
                html.Append("<div class=\"rcd\">").Append(Priority(rec.Priority)).Append(" <b>").Append(E(rec.ProjectCode)).Append("</b> · ")
                    .Append(E(rec.Action)).Append("<div class=\"sub\">").Append(E(rec.Owner)).Append(" · ").Append(E(rec.Due)).Append("</div></div>");
            html.Append("</div>");
        }

        if (analysis is { Watch.Count: > 0 })
        {
            html.Append("<h2 style=\"margin-top:14px\">").Append(r.Kind switch
            {
                ReportRun.Daily => "ما يجب متابعته غداً",
                ReportRun.Weekly => "ما يجب متابعته الأسبوع القادم",
                _ => "ما يجب متابعته الشهر القادم",
            }).Append("</h2><ul class=\"w\">");
            foreach (var w in analysis.Watch) html.Append("<li>").Append(E(w)).Append("</li>");
            html.Append("</ul>");
        }

        html.Append("<p class=\"method\">طريقة الحساب: نسبة الإنجاز = أيام العمل التي أُنجزت فيها الخطة مقسومة على الأيام المخططة لكل وحدة ")
            .Append("(خطة العميل: 18 يوماً لكل منطقة حفر، 14 لكل قاعدة، 17 لكل طابق، 6 لكل تخصص كهروميكانيكي في كل طابق). ")
            .Append("الانتهاء المتوقع يُحسب بوتيرة آخر 30 يوماً مع استثناء أيام الجمعة. ")
            .Append(analysis is null ? "" : "التحليل المكتوب أعده Claude من الأرقام أعلاه، وكل رقم فيه مطابق للبيانات.")
            .Append("</p><div class=\"foot\"><span>أُعد تلقائياً من منصة أساس · ").Append(E(ReportTexts.Date(r.To, weekday: true)))
            .Append("</span><span>asasksa.co</span></div></div></body></html>");
        return html.ToString();
    }

    // ------------------------------------------------------------------ sections

    private static void Hero(StringBuilder h, StakeholderReport r)
    {
        var p = r.Portfolio;
        h.Append("<div class=\"hero\"><div class=\"t\"><h1>").Append(E(ReportTexts.Title(r.Kind))).Append(" · تحليل أداء المشاريع</h1><span class=\"d\">")
            .Append(E(ReportTexts.Period(r))).Append("</span></div><div class=\"grid4\">");

        if (r.IsDaily)
        {
            Kpi(h, "تقارير اليوم", $"{p.Reports}<small>/{p.ExpectedReports}</small>",
                p.MissingReports > 0 ? $"<div class=\"dl down\">{p.MissingReports} لم يُرسل</div>" : "<div class=\"dl\">وصلت كلها</div>");
            Kpi(h, "العمالة", p.Crew.ToString(Inv), Delta(p.CrewChange, " عامل", "عن متوسط الأسبوعين"));
        }
        else
        {
            Kpi(h, "التقارير", p.Reports.ToString(Inv),
                p.MissingReports > 0 ? $"<div class=\"dl down\">{p.MissingReports} يوم عمل بلا تقرير</div>" : "<div class=\"dl\">لا أيام بلا تقرير</div>");
            Kpi(h, "متوسط العمالة اليومي", p.CrewAveragePerDay.ToString(Inv), Delta(p.CrewChange, " عامل", r.Kind == ReportRun.Weekly ? "عن الأسبوع السابق" : "عن الشهر السابق"));
        }
        Kpi(h, "أُنجزت الخطة", Pct(p.PlanMetPercent),
            Delta(p.PlanMetChangePoints, " نقطة", r.IsDaily ? "عن الأسبوعين" : r.Kind == ReportRun.Weekly ? "عن الأسبوع السابق" : "عن الشهر السابق"));
        Kpi(h, "تأخيرات · مشاكل", $"{p.Delays}<small> · </small>{p.Problems}", $"<div class=\"dl\">{(r.IsDaily ? "اليوم" : PeriodWord(r))}</div>");
        h.Append("</div></div>");
    }

    private static void Kpi(StringBuilder h, string label, string value, string sub) =>
        h.Append("<div class=\"k\"><div class=\"l\">").Append(label).Append("</div><div class=\"v num\">").Append(value).Append("</div>").Append(sub).Append("</div>");

    private static void Charts(StringBuilder h, StakeholderReport r, ReportAnalysis? analysis)
    {
        var seriesTitle = r.Kind switch
        {
            ReportRun.Daily => "آخر 14 يوم عمل",
            ReportRun.Weekly => "أيام الأسبوع",
            _ => "أسابيع الشهر",
        };
        h.Append("<div class=\"grid2\"><div class=\"card\"><h3>إنجاز الخطة · ").Append(seriesTitle).Append("</h3>")
            .Append(Bars(r.Series, r.IsDaily ? r.To : null));
        Note(h, analysis?.ChartNotes.PlanMet);
        h.Append("</div><div class=\"card\"><h3>").Append(r.Kind == ReportRun.Monthly ? "متوسط العمالة اليومي · " : "العمالة في المواقع · ")
            .Append(seriesTitle).Append("</h3>").Append(LineChart(r.Series));
        Note(h, analysis?.ChartNotes.Crew);
        h.Append("</div></div>");

        if (r.Trend.Count > 0)
            h.Append("<div class=\"card\" style=\"margin-bottom:14px\"><h3>إنجاز الخطة · ")
                .Append(r.Kind == ReportRun.Weekly ? "آخر 8 أسابيع" : "آخر 6 أشهر").Append("</h3>")
                .Append(Bars(r.Trend, null, height: 90)).Append("</div>");
    }

    private static void Project(StringBuilder h, StakeholderReport r, ReportProject p, ReportProjectAnalysis? a)
    {
        var tone = ToneColor(p.Tone, Accent);
        h.Append("<section class=\"pa\"><div class=\"pah\"><div><span class=\"code\">").Append(E(p.Code)).Append("</span> <b>").Append(p.Name == p.Code ? "" : E(p.Name))
            .Append("</b><span class=\"sub\"> · ").Append(E(p.City ?? "")).Append(p.Manager is null ? "" : " · " + E(p.Manager))
            .Append("</span></div><span class=\"pill\" style=\"background:").Append(tone).Append("\">").Append(ToneWord(p.Tone)).Append("</span></div>")
            .Append("<div class=\"pag\"><div class=\"pav\"><div class=\"big num\">").Append(p.WorkCompletePercent).Append("%<small> منجز</small></div>");
        if (!r.IsDaily && p.WorkCompleteChangePoints != 0)
            h.Append("<div class=\"dl up\">▲ ").Append(p.WorkCompleteChangePoints).Append(" نقطة ").Append(PeriodWord(r)).Append("</div>");
        if (p.TimeElapsedPercent is { } elapsed) h.Append("<div class=\"sub\">مضى ").Append(elapsed).Append("% من مدة العقد</div>");

        h.Append("<div class=\"pstrip\">");
        foreach (var s in p.Stages)
        {
            var current = p.CurrentStages.Contains(s.Label);
            h.Append("<div class=\"ps").Append(current ? " cur" : "").Append("\"><div class=\"psl\">").Append(E(ShortStage(s.Stage))).Append("</div>")
                .Append(Progress(s.CompletePercent, s.Finished ? Good : Accent)).Append("<div class=\"psv\">").Append(s.CompletePercent).Append("%</div></div>");
        }
        h.Append("</div>");
        Kv(h, "الانتهاء المتوقع", p.ForecastFinish is { } f ? ReportTexts.Date(f) : "—");
        Kv(h, "موعد العقد", p.ContractEnd is { } c ? ReportTexts.Date(c) : "—");
        Kv(h, "الفرق", $"<b style=\"color:{tone}\">{LateText(p.DaysLate)}</b>", raw: true);
        if (!r.IsDaily && p.DaysLateChange is { } change && change != 0)
            Kv(h, "تغيّر التوقع " + PeriodWord(r), change > 0 ? $"تأخر {change} يوماً" : $"تحسّن {-change} يوماً");
        Kv(h, "إنجاز الخطة (30 يوماً)", Pct(p.PlanMetPercentLast30Days));
        h.Append("</div><div class=\"pat\">");

        if (a is not null)
        {
            Para(h, "الوضع", a.Situation);
            Para(h, "لماذا", a.Why);
            Para(h, "المطلوب", a.Action, "act");
        }
        else
        {
            h.Append("<div class=\"dg\"><p>").Append(E(PlainProject(r, p))).Append("</p></div>");
        }

        var lostStages = p.Stages.Where(s => s.ReportDays > 0).ToList();
        if (lostStages.Any(s => s.LostDays > 0))
        {
            h.Append("<div class=\"dg\"><h4>أين ذهب الوقت</h4><div class=\"legend\"><i class=\"plan\"></i> المخطط (أيام عمل) <i class=\"act over\"></i> أيام التقارير</div>");
            var top = Math.Max(1, lostStages.Max(s => Math.Max(s.PlannedDays, s.ReportDays)));
            foreach (var s in lostStages)
            {
                h.Append("<div class=\"pva\"><div class=\"pval\">").Append(E(s.Label)).Append(s.Finished ? "" : " (جارية)").Append("</div><div class=\"pvab\">")
                    .Append("<div class=\"b plan\" style=\"width:").Append(Pc(s.PlannedDays, top)).Append("%\"></div>")
                    .Append("<div class=\"b ").Append(s.LostDays > 0 ? "act over" : "act").Append("\" style=\"width:").Append(Pc(s.ReportDays, top)).Append("%\"></div></div>")
                    .Append("<div class=\"pvav ").Append(s.LostDays > 0 ? "bad" : "good").Append("\"><span class=\"num\">").Append(s.ReportDays).Append(" يوماً · المخطط ").Append(s.PlannedDays)
                    .Append("</span><br>").Append(s.LostDays > 0 ? $"{s.LostDays} يوماً دون إنجاز الخطة" : "كل الأيام حسب الخطة").Append("</div></div>");
            }
            h.Append("</div>");
        }
        h.Append("</div></div>");

        if (r.IsDaily && (p.UnitReports.Count > 0 || p.MissingUnits.Count > 0))
        {
            h.Append("<table class=\"units hide-phone\"><tr><th>المرحلة</th><th>الوحدة</th><th class=\"c\">العمالة</th><th>الطقس</th><th class=\"c\">الخطة</th><th>ملاحظات</th></tr>");
            foreach (var u in p.UnitReports)
                h.Append("<tr><td>").Append(E(ShortStage(u.Stage))).Append("</td><td><b>").Append(E(u.Unit)).Append("</b></td><td class=\"c num\">").Append(u.Crew)
                    .Append("</td><td>").Append(E(u.Weather)).Append("</td><td class=\"c\">").Append(u.PlanMet ? "<span class=\"ok\">✓ نعم</span>" : "<span class=\"no\">✗ لا</span>")
                    .Append("</td><td>").Append(UnitNotes(u)).Append("</td></tr>");
            foreach (var m in p.MissingUnits)
                h.Append("<tr class=\"miss\"><td></td><td><b>").Append(E(m)).Append("</b></td><td colspan=\"4\">لم يصل تقرير اليوم (أُرسل في يوم العمل السابق)</td></tr>");
            h.Append("</table><div class=\"show-phone\">");
            foreach (var u in p.UnitReports)
                h.Append("<div class=\"rc\"><div class=\"rch\"><b>").Append(E(u.Unit)).Append("</b>").Append(u.PlanMet ? "<span class=\"ok\">✓ أُنجزت الخطة</span>" : "<span class=\"no\">✗ لم تُنجز</span>")
                    .Append("</div><div class=\"sub\">").Append(E(ShortStage(u.Stage))).Append(" · ").Append(u.Crew).Append(" عمال · ").Append(E(u.Weather)).Append("</div>").Append(u.DelayCause is null && u.Problem is null ? "" : UnitNotes(u)).Append("</div>");
            foreach (var m in p.MissingUnits) h.Append("<div class=\"rc miss\"><b>").Append(E(m)).Append("</b><div>لم يصل تقرير اليوم</div></div>");
            h.Append("</div>");
        }
        else if (!r.IsDaily)
        {
            h.Append("<div class=\"facts\"><div><span>تقارير ").Append(PeriodWord(r)).Append("</span><b class=\"num\">").Append(p.Reports).Append("</b></div>")
                .Append("<div><span>أُنجزت الخطة</span><b class=\"num\">").Append(Pct(p.PlanMetPercent)).Append("</b></div>")
                .Append("<div><span>متوسط العمالة</span><b class=\"num\">").Append(p.CrewAveragePerDay).Append("</b></div>")
                .Append("<div><span>أيام بلا تقرير</span><b class=\"num\">").Append(p.MissingReports).Append("</b></div></div>");
        }
        h.Append("</section>");
    }

    private static void Issues(StringBuilder h, StakeholderReport r)
    {
        if (r.Issues.Count == 0)
        {
            h.Append("<p class=\"sub\">لا شيء ").Append(r.IsDaily ? "اليوم" : PeriodWord(r)).Append(".</p>");
            return;
        }
        foreach (var i in r.Issues)
        {
            var (word, color) = i.Type switch
            {
                "problem" => ("مشكلة", Bad),
                "delay" => ("تأخير", Warn),
                _ => ("لم يُرسل", Soft),
            };
            h.Append("<div class=\"is\"><b style=\"color:").Append(color).Append("\">").Append(word).Append(" · ").Append(E(i.ProjectCode)).Append(' ').Append(E(i.Unit))
                .Append(r.IsDaily ? "" : " · " + i.Date.ToString("d/M", Inv)).Append("</b><div class=\"sub\">").Append(E(i.Text)).Append("</div></div>");
        }
    }

    // ------------------------------------------------------------------ text without an analysis

    private static string PlainSummary(StakeholderReport r)
    {
        var p = r.Portfolio;
        return $"عدد المشاريع التي تُرسل تقاريرها: {r.Projects.Count}. وصل {p.Reports} تقريراً {(r.IsDaily ? "اليوم" : PeriodWord(r))}، " +
               $"وأُنجزت الخطة في {Pct(p.PlanMetPercent)} منها، مع {p.Delays} تأخيرات و{p.Problems} مشاكل مسجلة.";
    }

    private static string PlainProject(StakeholderReport r, ReportProject p) =>
        $"المراحل الحالية: {string.Join(" + ", p.CurrentStages)}. الإنجاز {p.WorkCompletePercent}%، والانتهاء المتوقع " +
        $"{(p.ForecastFinish is { } f ? ReportTexts.Date(f) : "غير محدد")} ({LateText(p.DaysLate)}).";

    // ------------------------------------------------------------------ charts

    private static string Bars(IReadOnlyList<ReportSeriesPoint> series, DateOnly? highlight, int height = 120)
    {
        const int w = 520;
        var n = Math.Max(1, series.Count);
        var gap = n > 10 ? 6.0 : 14.0;
        var bw = (w - gap * (n - 1)) / n;
        var target = height * 0.2;
        var s = new StringBuilder();
        s.Append($"<svg viewBox=\"0 -18 {w} {height + 42}\" class=\"chart\"><line x1=\"0\" x2=\"{w}\" y1=\"{height}\" y2=\"{height}\" stroke=\"{Line}\"/>")
            .Append(Inv, $"<line x1=\"0\" x2=\"{w}\" y1=\"{target:0.#}\" y2=\"{target:0.#}\" stroke=\"{Line}\" stroke-dasharray=\"3 3\"/>")
            .Append(Inv, $"<text x=\"{w}\" y=\"{target - 4:0.#}\" text-anchor=\"end\" class=\"ax\">هدف 80%</text>");
        var every = n > 10 ? 2 : 1;
        for (var i = 0; i < series.Count; i++)
        {
            var point = series[i];
            var x = i * (bw + gap);
            if (point.PlanMetPercent is not { } pct)
            {
                s.Append(Inv, $"<rect x=\"{x:0.#}\" y=\"{height - 3}\" width=\"{bw:0.#}\" height=\"3\" fill=\"{Line}\"/>");
            }
            else
            {
                var bh = Math.Max(3, pct / 100.0 * height);
                var color = pct >= 80 ? Good : pct >= 50 ? Warn : Bad;
                s.Append(Inv, $"<rect x=\"{x:0.#}\" y=\"{height - bh:0.#}\" width=\"{bw:0.#}\" height=\"{bh:0.#}\" rx=\"2\" fill=\"{color}\"")
                    .Append(highlight == point.Date ? $" stroke=\"{Ink}\" stroke-width=\"1.5\"" : "").Append("/>");
                if (n <= 10) s.Append(Inv, $"<text x=\"{x + bw / 2:0.#}\" y=\"{height - bh - 4:0.#}\" text-anchor=\"middle\" class=\"ax b\">{pct}%</text>");
            }
            if ((series.Count - 1 - i) % every == 0)
                s.Append(Inv, $"<text x=\"{x + bw / 2:0.#}\" y=\"{height + 15}\" text-anchor=\"middle\" class=\"ax\">{E(point.Label)}</text>");
        }
        return s.Append("</svg>").ToString();
    }

    private static string LineChart(IReadOnlyList<ReportSeriesPoint> series)
    {
        const int w = 520, h = 110;
        var s = new StringBuilder($"<svg viewBox=\"-6 -16 {w + 12} {h + 40}\" class=\"chart\"><line x1=\"0\" x2=\"{w}\" y1=\"{h}\" y2=\"{h}\" stroke=\"{Line}\"/>");
        var withData = series.Where(p => p.Reports > 0).ToList();
        if (series.Count < 2 || withData.Count == 0) return s.Append("</svg>").ToString();

        var hi = Math.Max(1, series.Max(p => p.Crew)) * 1.15;
        var pts = series.Select((p, i) => (X: i * (double)w / (series.Count - 1), Y: h - p.Crew / hi * h, P: p)).ToList();
        var path = string.Join(" ", pts.Select((p, i) => string.Format(Inv, "{0}{1:0.#},{2:0.#}", i == 0 ? "M" : "L", p.X, p.Y)));
        s.Append(Inv, $"<path d=\"{path} L{w},{h} L0,{h} Z\" fill=\"{Accent}\" opacity=\".12\"/>")
            .Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{Accent}\" stroke-width=\"2.5\" stroke-linejoin=\"round\"/>");
        var every = series.Count > 10 ? 2 : 1;
        for (var i = 0; i < pts.Count; i++)
        {
            var (x, y, p) = pts[i];
            var last = i == pts.Count - 1;
            s.Append(Inv, $"<circle cx=\"{x:0.#}\" cy=\"{y:0.#}\" r=\"{(last ? 4 : 2.5)}\" fill=\"{(last ? Accent2 : Accent)}\"/>");
            if (last || i == 0 || series.Count <= 10)
                s.Append(Inv, $"<text x=\"{x:0.#}\" y=\"{y - 8:0.#}\" text-anchor=\"{(last ? "end" : i == 0 ? "start" : "middle")}\" class=\"ax b\">{p.Crew}</text>");
            if ((pts.Count - 1 - i) % every == 0)
                s.Append(Inv, $"<text x=\"{x:0.#}\" y=\"{h + 15}\" text-anchor=\"{(last ? "end" : i == 0 ? "start" : "middle")}\" class=\"ax\">{E(p.Label)}</text>");
        }
        return s.Append("</svg>").ToString();
    }

    private static void HBars(StringBuilder h, IReadOnlyList<ReportCount> items)
    {
        var top = Math.Max(1, items.Max(i => i.Count));
        foreach (var i in items)
            h.Append("<div class=\"hb\"><div class=\"hbl\">").Append(E(i.Label)).Append("</div><div class=\"hbt\"><div style=\"width:")
                .Append(Pc(i.Count, top)).Append("%\"></div></div><div class=\"hbv\">").Append(i.Count).Append("</div></div>");
    }

    private static string Progress(int percent, string color) =>
        $"<div class=\"pbar\"><div style=\"width:{Math.Clamp(percent, 0, 100)}%;background:{color}\"></div></div>";

    // ------------------------------------------------------------------ small helpers

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string E(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);

    private static string Pct(int? percent) => percent is { } p ? $"{p}%" : "—";

    private static int Pc(int value, int top) => (int)Math.Round(100.0 * value / top);

    private static string PeriodWord(StakeholderReport r) => r.Kind == ReportRun.Weekly ? "هذا الأسبوع" : "هذا الشهر";

    private static string LateText(int? days) => days switch
    {
        null => "غير محدد",
        > 0 => $"متأخر {days} يوماً",
        < 0 => $"قبل الموعد بـ {-days} يوماً",
        _ => "في الموعد",
    };

    private static string ToneWord(string tone) => tone switch
    {
        "good" => "ضمن الجدول",
        "warn" => "تأخر طفيف",
        "bad" => "متأخر",
        _ => "—",
    };

    private static string ToneColor(string? tone, string fallback) => tone switch
    {
        "good" => Good,
        "warn" => Warn,
        "bad" => Bad,
        _ => fallback,
    };

    private static string ShortStage(string stage) => stage switch
    {
        "paperwork" => "الأوراق",
        "excavation" => "الحفر",
        "foundation" => "الأساسات",
        "structural" => "الهيكل",
        "mep" => "ميكانيك",
        _ => stage,
    };

    private static string Priority(string priority) => priority switch
    {
        "high" => "<span class=\"pri h\">عالية</span>",
        "medium" => "<span class=\"pri m\">متوسطة</span>",
        _ => "<span class=\"pri l\">منخفضة</span>",
    };

    private static string Delta(int? change, string unit, string against)
    {
        if (change is not { } d) return "<div class=\"dl\">&nbsp;</div>";
        if (d == 0) return $"<div class=\"dl\">= {against}</div>";
        return $"<div class=\"dl {(d > 0 ? "up" : "down")}\">{(d > 0 ? "▲" : "▼")} {Math.Abs(d)}{unit} {against}</div>";
    }

    private static void Kv(StringBuilder h, string label, string value, bool raw = false) =>
        h.Append("<div class=\"kv\"><span>").Append(E(label)).Append("</span><b class=\"num\">").Append(raw ? value : E(value)).Append("</b></div>");

    private static void Para(StringBuilder h, string label, string text, string cls = "") =>
        h.Append("<div class=\"dg ").Append(cls).Append("\"><h4>").Append(label).Append("</h4><p>").Append(E(text)).Append("</p></div>");

    private static void Note(StringBuilder h, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text)) h.Append("<div class=\"note\"><b>القراءة:</b> ").Append(E(text)).Append("</div>");
    }

    private static string UnitNotes(ReportUnitDay u)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(u.DelayCause)) parts.Add($"<div><b class=\"w\">تأخير:</b> {E(u.DelayCause)}</div>");
        if (!string.IsNullOrWhiteSpace(u.Problem)) parts.Add($"<div><b class=\"x\">مشكلة:</b> {E(u.Problem)}</div>");
        return parts.Count == 0 ? "<span class=\"sub\">—</span>" : string.Concat(parts);
    }

    // ------------------------------------------------------------------ styles

    private static readonly string Css = $$"""
        @font-face{font-family:Tajawal;font-weight:400;src:url(Tajawal-Regular.ttf)}
        @font-face{font-family:Tajawal;font-weight:500;src:url(Tajawal-Medium.ttf)}
        @font-face{font-family:Tajawal;font-weight:700;src:url(Tajawal-Bold.ttf)}
        @font-face{font-family:Tajawal;font-weight:800;src:url(Tajawal-ExtraBold.ttf)}
        *{box-sizing:border-box;margin:0;padding:0}
        html{-webkit-print-color-adjust:exact;print-color-adjust:exact}
        body{font-family:Tajawal,sans-serif;color:{{Ink}};background:#fff;font-size:12.5px;line-height:1.55}
        body.phone .wrap{padding:18px 16px}
        .num{font-variant-numeric:tabular-nums}
        .chart{width:100%;height:auto;display:block;direction:ltr}
        .chart text{direction:ltr;unicode-bidi:isolate}
        .ax{font:11px Tajawal;fill:{{Soft}}} .ax.b{font-weight:700;fill:{{Ink}}}
        .hero{background:{{Navy}};color:#fff;border-radius:10px;padding:16px 18px;margin-bottom:14px}
        .hero .t{display:flex;justify-content:space-between;align-items:baseline;gap:8px;margin-bottom:12px}
        .hero h1{font-size:19px;font-weight:800} .hero .d{opacity:.75;font-size:12px}
        .grid4{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px}
        .k{background:rgba(255,255,255,.07);border-radius:8px;padding:10px 12px}
        .k .l{font-size:11px;opacity:.75} .k .v{font-size:28px;font-weight:800;line-height:1.15} .k .v small{font-size:13px;opacity:.6;font-weight:500}
        .dl{font-size:11px;font-weight:500;color:{{Soft}}} .k .dl{color:#C8D2DC} .k .dl.up{color:#7FD39A} .k .dl.down{color:#F29A8A}
        .dl.up{color:{{Good}}} .dl.down{color:{{Bad}}}
        .notice{background:#FFF4DE;color:#7A5212;border:1px solid #F0D49B;border-radius:6px;padding:7px 12px;font-size:12px;margin-bottom:12px}
        h2{font-size:15px;font-weight:800;margin:4px 0 10px;display:flex;align-items:center;gap:8px}
        h2:before{content:'';width:4px;height:16px;background:{{Accent2}};border-radius:2px}
        .summary{background:{{Bg}};border-radius:10px;padding:14px 16px;margin-bottom:14px;font-size:13.5px;line-height:1.85}
        .summary h3{font-size:12px;color:{{Accent}};font-weight:800;margin-bottom:4px}
        .fds{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-bottom:14px}
        .fd{display:grid;grid-template-columns:20px 64px 1fr;gap:8px;border:1px solid {{Line}};border-radius:10px;padding:10px;break-inside:avoid}
        .fdn{width:20px;height:20px;border-radius:50%;background:{{Ink}};color:#fff;font-size:11px;font-weight:700;display:grid;place-items:center}
        .fdb{font-size:21px;font-weight:800;line-height:1.1;text-align:center;direction:ltr;unicode-bidi:isolate}
        .fd b{font-size:12.5px} .fd p{font-size:11.5px;color:{{Soft}};line-height:1.65;margin-top:2px}
        .card{border:1px solid {{Line}};border-radius:10px;padding:12px;break-inside:avoid}
        .card h3{font-size:12px;font-weight:800;color:{{Soft}};margin-bottom:6px}
        .note{font-size:11.5px;line-height:1.7;margin-top:8px;padding-top:8px;border-top:1px dashed {{Line}}} .note b{color:{{Accent}}}
        .grid2{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:10px;margin-bottom:14px}
        .pa{border:1px solid {{Line}};border-radius:10px;padding:12px;margin-bottom:12px;break-inside:avoid}
        .pah{display:flex;justify-content:space-between;align-items:center;margin-bottom:10px;gap:8px}
        .code{background:{{Accent}};color:#fff;font-weight:700;border-radius:4px;padding:0 7px}
        .pill{display:inline-block;padding:1px 9px;border-radius:99px;font-size:11px;font-weight:700;color:#fff;white-space:nowrap}
        .sub{color:{{Soft}};font-size:10.5px}
        .pag{display:grid;grid-template-columns:250px minmax(0,1fr);gap:16px}
        .pav{background:{{Bg}};border-radius:8px;padding:10px;align-self:start}
        .big{font-size:28px;font-weight:800;line-height:1.1} .big small{font-size:12px;color:{{Soft}};font-weight:500}
        .pstrip{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:5px;margin:8px 0}
        .ps{font-size:10px;color:{{Soft}}} .ps.cur .psl{color:{{Accent}};font-weight:700}
        .psl{white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .pbar{background:#DDE1E5;border-radius:99px;overflow:hidden;height:6px} .pbar>div{height:100%;border-radius:99px}
        .kv{display:flex;justify-content:space-between;gap:8px;font-size:11px;padding:3px 0;border-top:1px solid {{Line}}} .kv span{color:{{Soft}}}
        .dg{margin-bottom:8px} .dg h4{font-size:11px;color:{{Soft}};font-weight:700} .dg p{font-size:12.5px;line-height:1.75}
        .dg.act{background:#EEF5FB;border-right:3px solid {{Accent}};padding:6px 10px;border-radius:4px} .dg.act h4{color:{{Accent}}}
        .legend{font-size:10.5px;color:{{Soft}};margin:6px 0} .legend i{display:inline-block;width:10px;height:8px;border-radius:2px;margin:0 2px 0 8px}
        .pva{display:grid;grid-template-columns:110px minmax(0,1fr) 120px;gap:6px;align-items:center;margin:4px 0;font-size:11px}
        .pvab .b{height:9px;border-radius:2px;margin:2px 0}
        .plan{background:#B9C7D3} .act{background:{{Good}}} .act.over{background:{{Bad}}}
        .pvav{font-weight:700;font-size:10.5px;line-height:1.35} .pvav span{font-weight:500;color:{{Soft}}}
        .pvav.bad{color:{{Bad}}} .pvav.good{color:{{Good}}}
        .facts{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;background:{{Bg}};border-radius:6px;padding:8px 10px;margin-top:10px}
        .facts span{display:block;font-size:10px;color:{{Soft}}} .facts b{font-size:14px}
        table{width:100%;border-collapse:collapse}
        th{text-align:right;font-size:10.5px;color:{{Soft}};font-weight:500;border-bottom:1px solid {{Line}};padding:5px 6px}
        td{padding:7px 6px;border-bottom:1px solid {{Line}};font-size:12px;vertical-align:top}
        table.units{margin-top:10px} .c{text-align:center}
        .ok{color:{{Good}};font-weight:700} .no{color:{{Bad}};font-weight:700} .w{color:{{Warn}}} .x{color:{{Bad}}}
        tr.miss td{background:#F7F0E6;color:#7A5212}
        .rc{border:1px solid {{Line}};border-radius:6px;padding:8px;margin-top:6px} .rch{display:flex;justify-content:space-between;gap:6px}
        .rc.miss{background:#F7F0E6;color:#7A5212}
        .hb{display:grid;grid-template-columns:1fr 2fr 28px;gap:8px;align-items:center;margin:5px 0;font-size:11.5px}
        .hbt{background:{{Bg}};height:8px;border-radius:99px;overflow:hidden} .hbt>div{height:100%;background:{{Accent2}}}
        .hbv{text-align:left;font-weight:700}
        .is{padding:6px 0;border-bottom:1px solid {{Bg}};font-size:11.5px}
        .pri{font-size:10.5px;font-weight:700;border-radius:4px;padding:1px 7px;color:#fff;white-space:nowrap}
        .pri.h{background:{{Bad}}} .pri.m{background:{{Warn}}} .pri.l{background:{{Soft}}}
        .rcd{border:1px solid {{Line}};border-radius:8px;padding:8px;margin-bottom:6px;font-size:12.5px}
        ul.w{padding-right:18px;font-size:12.5px;line-height:1.9;color:{{Ink}}}
        .pb{break-before:page}
        .method{font-size:10.5px;color:{{Soft}};line-height:1.7;margin-top:12px}
        .foot{margin-top:14px;padding-top:8px;border-top:1px solid {{Line}};font-size:10px;color:{{Soft}};display:flex;justify-content:space-between;gap:10px}
        .show-phone{display:none}
        body.phone{font-size:14px}
        body.phone .grid4{grid-template-columns:1fr 1fr}
        body.phone .fds,body.phone .grid2,body.phone .pag{grid-template-columns:1fr}
        body.phone .facts{grid-template-columns:1fr 1fr}
        body.phone .hero .t{flex-direction:column}
        body.phone .summary{font-size:14.5px}
        body.phone .pb{break-before:auto}
        body.phone .hide-phone{display:none}
        body.phone .show-phone{display:block}
        body.phone .pva{grid-template-columns:90px minmax(0,1fr) 100px}
        """;
}
