using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.Forms.Assignments.Commands.StopFormAssignment;

/// <summary>
/// Stops an assignment after today: today still counts, nothing after does, and the days so
/// far stay in the tracking list. One that hasn't started yet is removed outright.
/// </summary>
public record StopFormAssignmentCommand(Guid Id) : IRequest;

public class StopFormAssignmentCommandHandler : IRequestHandler<StopFormAssignmentCommand>
{
    private readonly IApplicationDbContext _db;

    public StopFormAssignmentCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(StopFormAssignmentCommand request, CancellationToken cancellationToken)
    {
        var assignment = await _db.FormAssignments.SingleOrDefaultAsync(a => a.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(FormAssignment), request.Id);
        assignment.Stop(FormAssignmentTracker.SaudiToday(DateTime.UtcNow));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
