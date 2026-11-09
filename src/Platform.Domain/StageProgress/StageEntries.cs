using Platform.Domain.Common;

namespace Platform.Domain.StageProgress;

/// <summary>
/// A unit's starting position when a project joins the stage model part-way (2026-11-10,
/// project 119). No date on purpose: a finished baseline unit counts as finished but has no
/// actual finish date, and an in-progress one carries its days earned so far. Reports added
/// afterwards build on it.
/// </summary>
public class StageBaselineEntry : AuditableEntity
{
    public Guid ProjectId { get; private set; }
    public string StageCode { get; private set; } = default!;
    public string? SubstepCode { get; private set; }
    public Guid? LevelId { get; private set; }
    public int? Side { get; private set; }
    public int EarnedDays { get; private set; }
    public bool Finished { get; private set; }

    private StageBaselineEntry() { }

    public static StageBaselineEntry Create(Guid projectId, string stageCode, string? substepCode, Guid? levelId, int? side, int earnedDays, bool finished)
    {
        if (string.IsNullOrWhiteSpace(stageCode)) throw new ArgumentException("A baseline needs a stage.", nameof(stageCode));
        if (earnedDays < 0) throw new ArgumentException("Earned days can't be negative.", nameof(earnedDays));
        if (levelId is not null && side is not null) throw new ArgumentException("A unit is a level or a side, not both.");
        return new StageBaselineEntry
        {
            ProjectId = projectId, StageCode = stageCode.Trim(), SubstepCode = string.IsNullOrWhiteSpace(substepCode) ? null : substepCode.Trim(),
            LevelId = levelId, Side = side, EarnedDays = earnedDays, Finished = finished,
        };
    }
}

/// <summary>An administrator reopened a finished unit so it can be reported again (2026-11-10).</summary>
public class StageUnitReopening : AuditableEntity
{
    public Guid ProjectId { get; private set; }
    public string StageCode { get; private set; } = default!;
    public string? SubstepCode { get; private set; }
    public Guid? LevelId { get; private set; }
    public int? Side { get; private set; }
    public DateOnly ReopenedOn { get; private set; }
    public string? Reason { get; private set; }

    private StageUnitReopening() { }

    public static StageUnitReopening Create(Guid projectId, string stageCode, string? substepCode, Guid? levelId, int? side, DateOnly reopenedOn, string? reason) =>
        new()
        {
            ProjectId = projectId, StageCode = stageCode.Trim(), SubstepCode = string.IsNullOrWhiteSpace(substepCode) ? null : substepCode.Trim(),
            LevelId = levelId, Side = side, ReopenedOn = reopenedOn, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 500)],
        };
}
