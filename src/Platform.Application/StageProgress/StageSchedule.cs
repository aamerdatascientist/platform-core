namespace Platform.Application.StageProgress;

/// <summary>
/// One unit of work: a stage (or one of its sub-steps) on one level, on one outside side, or
/// once for the whole project (fixed stages). Units exist only for stages that apply to the
/// project and are active, and only for the active levels in the stage's level scope.
/// </summary>
public sealed record StageUnit(StagePlanRow Row, Guid? LevelId, int? Side)
{
    public string StageCode => Row.StageCode;
    public string? SubstepCode => Row.SubstepCode;
    public int PlannedDays => Row.Days;
    public string Key => UnitKeys.For(Row.StageCode, Row.SubstepCode, LevelId, Side);
}

public static class UnitKeys
{
    public static string For(string stageCode, string? substepCode, Guid? levelId, int? side) =>
        $"{stageCode}|{substepCode}|" + (levelId is { } l ? "L" + l.ToString("N") : side is { } s ? "S" + s : "P");
}

/// <summary>A span on the planned timeline in working days: <c>Start</c> days of the project
/// are behind it when it begins, and it ends once <c>End</c> days have passed.</summary>
public readonly record struct DaySpan(int Start, int End);

/// <summary>Planned windows of one stage or unit, at the two ends of the range: fastest = every
/// floor or side at once, slowest = one at a time. Structure and walls are the same in both.</summary>
public readonly record struct PlannedWindow(DaySpan Fastest, DaySpan Slowest)
{
    /// <summary>The average of the two (owner, 2026-11-10: "ahead/behind" compares against it), rounded up.</summary>
    public DaySpan Middle => new(Mid(Fastest.Start, Slowest.Start), Mid(Fastest.End, Slowest.End));

    private static int Mid(int a, int b) => (a + b + 1) / 2;
}

public sealed record StageWindow(string StageCode, string NameEn, string NameAr, PlannedWindow Window, int PlannedUnitDays);

public sealed record PlannedSchedule(
    IReadOnlyList<StageUnit> Units,
    IReadOnlyDictionary<string, PlannedWindow> UnitWindows,
    IReadOnlyList<StageWindow> Stages,
    PlannedWindow Project,
    /// <summary>The end of walls (or of structure when there is no walls stage): the skeleton is ready.</summary>
    PlannedWindow? Skeleton)
{
    /// <summary>Σ planned days over every unit - the denominator of work complete.</summary>
    public int PlannedUnitDays => Units.Sum(u => u.PlannedDays);
}

/// <summary>
/// Builds a project's units and planned schedule from its stage plan (pure; no dates - see
/// <see cref="StageDates"/> for turning working-day offsets into dates).
///
/// Stages run in sort order, each starting when the previous one ends; a stage's sub-steps run
/// in their sort order. A per-floor or per-side stage takes its days once in the fastest case
/// (all floors or sides at once) and days × units in the slowest (one at a time, every
/// sub-step on one floor before the next floor). The plan never assumes how many run in
/// parallel - the daily reports say that. Structure and walls are the exception and take the
/// same time in both cases: structure goes level by level, parking first, and walls on level
/// i start at the later of structure starting on level i+2 (or all structure finishing, when
/// there is no level i+2) and walls finishing on level i-1.
/// </summary>
public static class StageScheduleBuilder
{
    public static IReadOnlyList<StageUnit> BuildUnits(IEnumerable<StagePlanRow> plan, ProjectShape shape) =>
        Applicable(plan, shape).SelectMany(row => UnitsFor(row, shape)).ToList();

    /// <summary>Active rows that apply to the project, in sort order.</summary>
    /// <remarks>A second active row for the same stage + step (the plan forms refuse one, but data
    /// saved another way could hold it) is ignored - the first by sort order wins.</remarks>
    public static IReadOnlyList<StagePlanRow> Applicable(IEnumerable<StagePlanRow> plan, ProjectShape shape) =>
        plan.Where(r => r.IsActive && r.Days > 0 && shape.Applies(r))
            .OrderBy(r => r.SortOrder)
            .DistinctBy(r => (r.StageCode, r.SubstepCode))
            .ToList();

    private static IEnumerable<StageUnit> UnitsFor(StagePlanRow row, ProjectShape shape) => row.DurationType switch
    {
        DurationType.PerFloor => shape.LevelsFor(row.LevelScope).Select(l => new StageUnit(row, l.Id, null)),
        DurationType.PerSide => Enumerable.Range(1, Math.Max(0, shape.OutsideSides)).Select(s => new StageUnit(row, null, s)),
        _ => [new StageUnit(row, null, null)],
    };

    public static PlannedSchedule Build(IEnumerable<StagePlanRow> plan, ProjectShape shape)
    {
        var rows = Applicable(plan, shape);
        var stages = rows.GroupBy(r => r.StageCode).Select(g => g.OrderBy(r => r.SortOrder).ToList()).ToList();
        // Stage order = the first sort_order of each stage (GroupBy keeps first-seen order).

        var units = new List<StageUnit>();
        var windows = new Dictionary<string, PlannedWindow>();
        var stageWindows = new List<StageWindow>();
        int fast = 0, slow = 0;
        PlannedWindow? skeleton = null;

        for (var i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            var code = stage[0].StageCode;
            var stageUnits = stage.SelectMany(r => UnitsFor(r, shape)).ToList();
            units.AddRange(stageUnits);

            if (code == StageCodes.Structure)
            {
                var walls = i + 1 < stages.Count && stages[i + 1][0].StageCode == StageCodes.Walls ? stages[i + 1] : null;
                var (structureEnd, end) = PlaceStructureAndWalls(stage, walls, shape, fast, slow, windows, units);
                stageWindows.Add(Window(stage, fast, slow, fast + structureEnd, slow + structureEnd, stageUnits));
                if (walls is not null)
                {
                    var wallUnits = units.Where(u => u.StageCode == StageCodes.Walls).ToList();
                    var wallStart = wallUnits.Count == 0 ? structureEnd : wallUnits.Min(u => windows[u.Key].Fastest.Start) - fast;
                    stageWindows.Add(Window(walls, fast + wallStart, slow + wallStart, fast + end, slow + end, wallUnits));
                    i++;
                }
                fast += end;
                slow += end;
                skeleton = new PlannedWindow(new DaySpan(0, fast), new DaySpan(0, slow));
                continue;
            }

            var (fastLength, slowLength) = PlaceStage(stage, shape, fast, slow, windows);
            stageWindows.Add(Window(stage, fast, slow, fast + fastLength, slow + slowLength, stageUnits));
            fast += fastLength;
            slow += slowLength;
            if (code == StageCodes.Walls) skeleton = new PlannedWindow(new DaySpan(0, fast), new DaySpan(0, slow));
        }

        return new PlannedSchedule(
            units, windows, stageWindows,
            new PlannedWindow(new DaySpan(0, fast), new DaySpan(0, slow)),
            skeleton);
    }

    private static StageWindow Window(List<StagePlanRow> stage, int fastStart, int slowStart, int fastEnd, int slowEnd, List<StageUnit> stageUnits) =>
        new(stage[0].StageCode, stage[0].StageNameEn, stage[0].StageNameAr,
            new PlannedWindow(new DaySpan(fastStart, fastEnd), new DaySpan(slowStart, slowEnd)),
            stageUnits.Sum(u => u.PlannedDays));

    /// <summary>Places one ordinary stage starting at (fast, slow); returns its two lengths.</summary>
    private static (int Fast, int Slow) PlaceStage(
        List<StagePlanRow> stage, ProjectShape shape, int fast, int slow, Dictionary<string, PlannedWindow> windows)
    {
        // Fastest: each sub-step on every floor/side at once, sub-steps one after another.
        var fastOffset = 0;
        var fastSpans = new Dictionary<StagePlanRow, DaySpan>();
        foreach (var row in stage)
        {
            fastSpans[row] = new DaySpan(fast + fastOffset, fast + fastOffset + row.Days);
            fastOffset += row.Days;
        }

        // Slowest: one floor/side at a time, all its sub-steps before the next. Fixed rows
        // (one unit) take their days once, in their place in the sequence.
        var slots = Math.Max(1, stage.Where(r => r.DurationType != DurationType.Fixed).Select(r => UnitsFor(r, shape).Count()).DefaultIfEmpty(1).Max());
        var slowOffset = 0;
        for (var slot = 0; slot < slots; slot++)
        {
            foreach (var row in stage)
            {
                var rowUnits = UnitsFor(row, shape).ToList();
                if (row.DurationType == DurationType.Fixed)
                {
                    if (slot != 0) continue;
                }
                else if (slot >= rowUnits.Count) continue;

                var unit = row.DurationType == DurationType.Fixed ? rowUnits[0] : rowUnits[slot];
                var slowSpan = new DaySpan(slow + slowOffset, slow + slowOffset + row.Days);
                windows[unit.Key] = new PlannedWindow(fastSpans[row], slowSpan);
                slowOffset += row.Days;
            }
        }
        return (fastOffset, slowOffset);
    }

    /// <summary>
    /// Structure level by level, and walls overlapping it two levels behind. Returns the
    /// structure's length and the length of the two together, measured from their start.
    /// </summary>
    private static (int StructureEnd, int End) PlaceStructureAndWalls(
        List<StagePlanRow> structure, List<StagePlanRow>? walls, ProjectShape shape, int fast, int slow,
        Dictionary<string, PlannedWindow> windows, List<StageUnit> units)
    {
        var levels = structure.Where(r => r.DurationType == DurationType.PerFloor).Select(r => shape.LevelsFor(r.LevelScope)).FirstOrDefault()
            ?? shape.LevelsFor(LevelScope.AllLevels);
        var structureStart = new Dictionary<Guid, int>();
        var t = 0;
        foreach (var level in levels)
        {
            structureStart[level.Id] = t;
            foreach (var row in structure) { Set(windows, row, level.Id, fast, slow, t, row.Days); t += row.Days; }
        }
        var structureEnd = t;
        // Fixed rows inside the structure stage (unusual) run after all levels.
        foreach (var row in structure.Where(r => r.DurationType == DurationType.Fixed))
        {
            Set(windows, row, null, fast, slow, t, row.Days);
            t += row.Days;
            structureEnd = t;
        }
        if (walls is null) return (structureEnd, structureEnd);

        units.AddRange(walls.SelectMany(r => UnitsFor(r, shape)));
        var wallLevels = walls.Where(r => r.DurationType == DurationType.PerFloor).Select(r => shape.LevelsFor(r.LevelScope)).FirstOrDefault()
            ?? Array.Empty<ProjectLevel>();
        var previousWallsEnd = 0;
        var end = structureEnd;
        foreach (var level in wallLevels)
        {
            var index = levels.ToList().FindIndex(l => l.Id == level.Id);
            var twoUp = index >= 0 && index + 2 < levels.Count ? structureStart[levels[index + 2].Id] : structureEnd;
            var start = Math.Max(twoUp, previousWallsEnd);
            var w = start;
            foreach (var row in walls.Where(r => r.DurationType == DurationType.PerFloor)) { Set(windows, row, level.Id, fast, slow, w, row.Days); w += row.Days; }
            previousWallsEnd = w;
            end = Math.Max(end, w);
        }
        foreach (var row in walls.Where(r => r.DurationType != DurationType.PerFloor))
        {
            var start = end;
            foreach (var unit in UnitsFor(row, shape)) { windows[unit.Key] = Same(fast, slow, start, row.Days); }
            end = start + row.Days;
        }
        return (structureEnd, end);
    }

    private static void Set(Dictionary<string, PlannedWindow> windows, StagePlanRow row, Guid? levelId, int fast, int slow, int at, int days) =>
        windows[UnitKeys.For(row.StageCode, row.SubstepCode, levelId, null)] = Same(fast, slow, at, days);

    private static PlannedWindow Same(int fast, int slow, int at, int days) =>
        new(new DaySpan(fast + at, fast + at + days), new DaySpan(slow + at, slow + at + days));
}
