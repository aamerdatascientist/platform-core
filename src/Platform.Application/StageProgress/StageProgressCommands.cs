using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.StageProgress;

namespace Platform.Application.StageProgress;

public record StageUnitDto(
    string Key, string StageCode, string? SubstepCode, Guid? LevelId, int? Side, string StageNameAr, string StageNameEn,
    string? SubstepNameAr, string? SubstepNameEn, string? LevelNameAr, string? LevelNameEn,
    int PlannedDays, int EarnedDays, bool Finished, DateOnly? ActualStart, DateOnly? ActualFinish, DateOnly? PlannedFinish,
    /// <summary>Actual finish minus planned finish in working days; positive = late. Null until both exist.</summary>
    int? LateByDays = null);

public record ProjectProgressDto(
    Guid ProjectId, string Code, string Name, string? Status, DateOnly? StartDate,
    int EarnedDays, int PlannedDays, double WorkComplete, double? PlannedShare, int ElapsedWorkingDays,
    DateOnly? FinishFastest, DateOnly? FinishSlowest, DateOnly? FinishTarget,
    int? SkeletonFastest, int? SkeletonSlowest, int TotalFastest, int TotalSlowest,
    IReadOnlyList<StageUnitDto> Units);

/// <summary>A project in the Stage progress chooser (admin flows, 2026-11-14).</summary>
public record StageProjectSummaryDto(Guid Id, string Code, string Name, string? Status);

/// <summary>Every project, by code - the Stage progress menu item lists them (administrators).</summary>
public record ListStageProjectsQuery : IRequest<IReadOnlyList<StageProjectSummaryDto>>;

public class ListStageProjectsQueryHandler(StageProgressData data) : IRequestHandler<ListStageProjectsQuery, IReadOnlyList<StageProjectSummaryDto>>
{
    public async Task<IReadOnlyList<StageProjectSummaryDto>> Handle(ListStageProjectsQuery request, CancellationToken cancellationToken) =>
        (await data.ProjectsAsync(cancellationToken))
            .OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .Select(p => new StageProjectSummaryDto(p.Id, p.Code, p.Name, p.Status))
            .ToList();
}

/// <summary>A project's progress by unit (administrators: the reopen buttons live on it).</summary>
public record GetProjectProgressQuery(Guid ProjectId) : IRequest<ProjectProgressDto>;

public class GetProjectProgressQueryHandler(StageProgressData data) : IRequestHandler<GetProjectProgressQuery, ProjectProgressDto>
{
    public async Task<ProjectProgressDto> Handle(GetProjectProgressQuery request, CancellationToken cancellationToken)
    {
        var project = await data.ProjectDataAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);
        var progress = ProjectProgressCalculator.Compute(project, StageProgressData.SaudiToday());
        var levels = project.Project.Shape.Levels.ToDictionary(l => l.Id);
        var units = progress.Units.Select(u =>
        {
            var level = u.Unit.LevelId is { } id ? levels.GetValueOrDefault(id) : null;
            var window = progress.Schedule.UnitWindows.TryGetValue(u.Unit.Key, out var w) ? w : (PlannedWindow?)null;
            DateOnly? planned = progress.Dates is { } dates && window is { } win ? dates.Finish(win.Middle.End) : null;
            int? late = planned is { } p && u.ActualFinish is { } a && progress.Dates is { } d
                ? a > p ? d.Calendar.WorkingDaysBetween(p.AddDays(1), a) : -d.Calendar.WorkingDaysBetween(a.AddDays(1), p)
                : null;
            return new StageUnitDto(u.Unit.Key, u.Unit.StageCode, u.Unit.SubstepCode, u.Unit.LevelId, u.Unit.Side,
                u.Unit.Row.StageNameAr, u.Unit.Row.StageNameEn, u.Unit.Row.SubstepNameAr, u.Unit.Row.SubstepNameEn,
                level?.LabelAr ?? (u.Unit.Side is { } s ? $"الواجهة {s}" : null), level?.LabelEn ?? (u.Unit.Side is { } s2 ? $"Side {s2}" : null),
                u.Unit.PlannedDays, u.EarnedDays, u.Finished, u.ActualStart, u.ActualFinish, planned, late);
        }).ToList();
        return new ProjectProgressDto(project.Project.Id, project.Project.Code, project.Project.Name, project.Project.Status, project.Project.StartDate,
            progress.EarnedDays, progress.PlannedDays, progress.WorkComplete, progress.PlannedShare, progress.ElapsedWorkingDays,
            progress.FinishFastest, progress.FinishSlowest, progress.FinishTarget,
            progress.Schedule.Skeleton?.Fastest.End, progress.Schedule.Skeleton?.Slowest.End,
            progress.Schedule.Project.Fastest.End, progress.Schedule.Project.Slowest.End, units);
    }
}

/// <summary>An administrator reopens a finished unit so it can be reported again.</summary>
public record ReopenStageUnitCommand(Guid ProjectId, string StageCode, string? SubstepCode, Guid? LevelId, int? Side, string? Reason) : IRequest;

public class ReopenStageUnitCommandValidator : AbstractValidator<ReopenStageUnitCommand>
{
    public ReopenStageUnitCommandValidator()
    {
        RuleFor(x => x.StageCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public class ReopenStageUnitCommandHandler(IApplicationDbContext db, StageProgressData data) : IRequestHandler<ReopenStageUnitCommand>
{
    public async Task Handle(ReopenStageUnitCommand request, CancellationToken cancellationToken)
    {
        var project = await data.ProjectDataAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);
        var progress = ProjectProgressCalculator.Compute(project, StageProgressData.SaudiToday());
        var key = UnitKeys.For(request.StageCode, request.SubstepCode, request.LevelId, request.Side);
        var unit = progress.Units.FirstOrDefault(u => u.Unit.Key == key)
            ?? throw new Common.Exceptions.ValidationException("stage.unitMissing", "That stage isn't in this project's plan there.");
        if (!unit.Finished)
            throw new Common.Exceptions.ValidationException("stage.unitNotFinished", "That stage isn't finished there, so there's nothing to reopen.");
        db.StageUnitReopenings.Add(StageUnitReopening.Create(request.ProjectId, request.StageCode, request.SubstepCode, request.LevelId, request.Side,
            StageProgressData.SaudiToday(), request.Reason));
        await db.SaveChangesAsync(cancellationToken);
    }
}

public record StageBaselineInput(string StageCode, string? SubstepCode, Guid? LevelId, int? Side, int EarnedDays, bool Finished);

/// <summary>
/// Replaces a project's starting position (owner, 2026-11-10: for projects that join the stage
/// model part-way, e.g. 119). Every entry must name a unit of the project's plan.
/// </summary>
public record SetStageBaselineCommand(Guid ProjectId, IReadOnlyList<StageBaselineInput> Entries) : IRequest<int>;

public class SetStageBaselineCommandHandler(IApplicationDbContext db, StageProgressData data) : IRequestHandler<SetStageBaselineCommand, int>
{
    public async Task<int> Handle(SetStageBaselineCommand request, CancellationToken cancellationToken)
    {
        var project = await data.ProjectDataAsync(request.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", request.ProjectId);
        var units = StageScheduleBuilder.BuildUnits(project.Plan, project.Project.Shape).ToDictionary(u => u.Key);
        foreach (var e in request.Entries)
        {
            var key = UnitKeys.For(e.StageCode, e.SubstepCode, e.LevelId, e.Side);
            if (!units.TryGetValue(key, out var unit))
                throw new Common.Exceptions.ValidationException("stage.unitMissing", $"'{e.StageCode}' isn't in the project's plan for that floor or side.");
            if (e.EarnedDays < 0 || e.EarnedDays > unit.PlannedDays)
                throw new Common.Exceptions.ValidationException("stage.baselineDays", $"'{e.StageCode}' can't have {e.EarnedDays} days earned (planned {unit.PlannedDays}).");
        }
        if (request.Entries.Select(e => UnitKeys.For(e.StageCode, e.SubstepCode, e.LevelId, e.Side)).Distinct().Count() != request.Entries.Count)
            throw new Common.Exceptions.ValidationException("stage.baselineDuplicate", "Each unit can appear once in the starting position.");

        var old = await db.StageBaselines.Where(b => b.ProjectId == request.ProjectId).ToListAsync(cancellationToken);
        foreach (var b in old) db.StageBaselines.Remove(b);
        foreach (var e in request.Entries)
            db.StageBaselines.Add(StageBaselineEntry.Create(request.ProjectId, e.StageCode, e.SubstepCode, e.LevelId, e.Side, e.EarnedDays, e.Finished));
        await db.SaveChangesAsync(cancellationToken);
        return request.Entries.Count;
    }
}

/// <summary>Re-runs a project's setup (floors, plan copy) - safe to repeat.</summary>
public record EnsureProjectSetupCommand(Guid ProjectId, Guid UserId) : IRequest;

public class EnsureProjectSetupCommandHandler(ProjectSetup setup) : IRequestHandler<EnsureProjectSetupCommand>
{
    public Task Handle(EnsureProjectSetupCommand request, CancellationToken cancellationToken) =>
        setup.EnsureAsync(request.ProjectId, request.UserId, cancellationToken);
}
