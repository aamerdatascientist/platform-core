using MediatR;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Assignments.Queries.GetMyAssignments;

/// <summary>The signed-in user's assigned days: today and the <see cref="DaysBack"/> days before it.</summary>
public record MyAssignmentsDto(DateOnly Today, IReadOnlyList<AssignmentDayDto> Days);

public record GetMyAssignmentsQuery : IRequest<MyAssignmentsDto>
{
    public const int DaysBack = 13;
}

public class GetMyAssignmentsQueryHandler : IRequestHandler<GetMyAssignmentsQuery, MyAssignmentsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _dynamicData;
    private readonly ICurrentUserService _currentUser;

    public GetMyAssignmentsQueryHandler(IApplicationDbContext db, IDynamicDataRepository dynamicData, ICurrentUserService currentUser)
    {
        _db = db;
        _dynamicData = dynamicData;
        _currentUser = currentUser;
    }

    public async Task<MyAssignmentsDto> Handle(GetMyAssignmentsQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException();
        var today = FormAssignmentTracker.SaudiToday(DateTime.UtcNow);
        var from = today.AddDays(-GetMyAssignmentsQuery.DaysBack);

        var (assignments, stamps) = await FormAssignmentTracker.LoadAsync(
            _db, _dynamicData, userId, null, from, today, cancellationToken);
        return new MyAssignmentsDto(today, FormAssignmentTracker.BuildDays(assignments, stamps, from, today, today));
    }
}
