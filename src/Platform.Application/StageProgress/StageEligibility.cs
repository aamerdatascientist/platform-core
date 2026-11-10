namespace Platform.Application.StageProgress;

/// <summary>Why one block of a daily report can't be accepted. <see cref="Code"/> is the
/// apiErrors.* key the app translates; <see cref="Field"/> the block field it points at.</summary>
public sealed record StageProblem(int BlockIndex, string Field, string Code, string Message);

/// <summary>
/// The daily report's stage rules, enforced on the server (owner, 2026-11-10), pure so they
/// can be tested without a database:
/// - the stage must be in the project's plan (active, applying to the project), and a stage
///   with sub-steps needs one of its own sub-steps;
/// - the same stage + sub-step can't appear twice in one report;
/// - structure names exactly one level, other per-floor stages at least one floor in the
///   stage's level scope, per-side stages at least one side 1..N, fixed stages neither;
/// - a unit already finished can't be reported again unless an administrator reopened it;
/// - on one floor (side, or the whole project) a later sub-step can't be reported before the
///   earlier one has started - in an earlier report or earlier in this one.
/// </summary>
public static class StageEligibility
{
    public const string NotInPlan = "stage.notInPlan";
    public const string SubstepNeeded = "stage.substepNeeded";
    public const string Duplicate = "stage.duplicate";
    public const string UnitsNeeded = "stage.unitsNeeded";
    public const string UnitFinished = "stage.unitFinished";
    public const string SubstepOrder = "stage.substepOrder";

    public static IReadOnlyList<StageProblem> Check(
        PlannedSchedule schedule, IReadOnlyList<StageReportBlock> blocks, IReadOnlyList<UnitProgress> existing)
    {
        var problems = new List<StageProblem>();
        var units = schedule.Units.ToDictionary(u => u.Key);
        var progress = existing.ToDictionary(u => u.Unit.Key);
        var rowsByStage = schedule.Units.Select(u => u.Row).Distinct().GroupBy(r => r.StageCode)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.SortOrder).ToList());
        var seen = new HashSet<(string, string?)>();
        // Every unit this report names, whatever the block order: an earlier step reported in the
        // same report counts as started, before or after the later step's block.
        var startedHere = new HashSet<string>();
        foreach (var b in blocks)
            if (rowsByStage.TryGetValue(b.StageCode, out var rs) && rs.FirstOrDefault(r => r.SubstepCode == b.SubstepCode) is { } br)
                foreach (var key in UnitsOf(b, br).Keys) startedHere.Add(key);

        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            if (!rowsByStage.TryGetValue(b.StageCode, out var rows))
            {
                problems.Add(new(i, StageForms.Report.Stage, NotInPlan, "This stage isn't in the project's plan."));
                continue;
            }
            var hasSubsteps = rows.Any(r => r.SubstepCode is not null);
            var row = rows.FirstOrDefault(r => r.SubstepCode == (hasSubsteps ? b.SubstepCode : null));
            if (row is null)
            {
                problems.Add(new(i, StageForms.Report.Substep, SubstepNeeded, "Choose one of this stage's sub-steps."));
                continue;
            }
            if (!seen.Add((b.StageCode, row.SubstepCode)))
            {
                problems.Add(new(i, StageForms.Report.Stage, Duplicate, "This stage is already in this report."));
                continue;
            }

            var (field, keys) = UnitsOf(b, row);
            var count = b.StageCode == StageCodes.Structure ? b.LevelIds.Count == 1 : keys.Count > 0;
            if (!count || keys.Any(k => !units.ContainsKey(k)))
            {
                problems.Add(new(i, field, UnitsNeeded, "Choose where this stage was worked today."));
                continue;
            }

            foreach (var key in keys)
            {
                if (progress.TryGetValue(key, out var p) && p.Finished)
                {
                    problems.Add(new(i, field, UnitFinished, "This stage is already finished there."));
                    break;
                }
                var index = rows.IndexOf(row);
                if (index > 0)
                {
                    var earlierKey = key.Replace($"{b.StageCode}|{row.SubstepCode}|", $"{b.StageCode}|{rows[index - 1].SubstepCode}|", StringComparison.Ordinal);
                    var earlierStarted = (progress.TryGetValue(earlierKey, out var e) && e.Started) || startedHere.Contains(earlierKey);
                    if (!earlierStarted)
                    {
                        problems.Add(new(i, StageForms.Report.Substep, SubstepOrder, "The previous sub-step hasn't started there yet."));
                        break;
                    }
                }
            }
        }
        return problems;
    }

    private static (string Field, IReadOnlyList<string> Keys) UnitsOf(StageReportBlock b, StagePlanRow row) => row.DurationType switch
    {
        DurationType.PerFloor => (b.StageCode == StageCodes.Structure ? StageForms.Report.Level : StageForms.Report.Floors,
            b.LevelIds.Distinct().Select(l => UnitKeys.For(b.StageCode, row.SubstepCode, l, null)).ToList()),
        DurationType.PerSide => (StageForms.Report.Sides,
            b.Sides.Distinct().Select(s => UnitKeys.For(b.StageCode, row.SubstepCode, null, s)).ToList()),
        _ => (StageForms.Report.Stage, [UnitKeys.For(b.StageCode, row.SubstepCode, null, null)]),
    };
}
