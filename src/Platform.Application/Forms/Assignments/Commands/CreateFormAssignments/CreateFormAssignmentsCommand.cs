using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Notifications;
using Platform.Domain.Forms;
using ValidationException = Platform.Application.Common.Exceptions.ValidationException;

namespace Platform.Application.Forms.Assignments.Commands.CreateFormAssignments;

/// <summary>
/// Assigns one form to one or more users, to fill every working day from StartDate (today at
/// the earliest) to EndDate (open-ended when null). A user who already has an assignment for
/// this form overlapping those dates is skipped and named in the result, not assigned twice.
/// </summary>
public record CreateFormAssignmentsCommand(
    Guid FormDefinitionId, IReadOnlyList<Guid> UserIds, DateOnly StartDate, DateOnly? EndDate, string? Note)
    : IRequest<CreateFormAssignmentsResult>;

public record CreateFormAssignmentsResult(int Created, IReadOnlyList<string> AlreadyAssigned);

public class CreateFormAssignmentsCommandValidator : AbstractValidator<CreateFormAssignmentsCommand>
{
    public CreateFormAssignmentsCommandValidator()
    {
        RuleFor(x => x.UserIds).NotEmpty().WithMessage("Choose at least one user.");
        RuleFor(x => x.UserIds.Count).LessThanOrEqualTo(200);
        RuleFor(x => x.StartDate)
            .Must(start => start >= FormAssignmentTracker.SaudiToday(DateTime.UtcNow))
            .WithMessage("The start date can't be in the past.");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).When(x => x.EndDate is not null)
            .WithMessage("The end date can't be before the start date.");
        RuleFor(x => x.Note).MaximumLength(400);
    }
}

public class CreateFormAssignmentsCommandHandler : IRequestHandler<CreateFormAssignmentsCommand, CreateFormAssignmentsResult>
{
    private readonly IApplicationDbContext _db;
    private readonly AssignmentNotifier _notifier;

    public CreateFormAssignmentsCommandHandler(IApplicationDbContext db, AssignmentNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

    public async Task<CreateFormAssignmentsResult> Handle(CreateFormAssignmentsCommand request, CancellationToken cancellationToken)
    {
        var form = await _db.FormDefinitions
            .Include(f => f.Versions)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken)
            ?? throw new NotFoundException(nameof(FormDefinition), request.FormDefinitionId);
        if (form.GetPublishedVersion() is null || form.TableName is null)
            throw new ValidationException("assignment.formNotPublished", $"'{form.Name}' isn't published yet, so it can't be filled.");

        var userIds = request.UserIds.Distinct().ToList();
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.IsActive })
            .ToListAsync(cancellationToken);
        if (users.Count != userIds.Count)
            throw new NotFoundException("User", string.Join(", ", userIds.Except(users.Select(u => u.Id))));
        var inactive = users.Where(u => !u.IsActive).Select(u => u.DisplayName).ToList();
        if (inactive.Count > 0)
            throw new ValidationException("assignment.userInactive", $"Deactivated users can't be assigned: {string.Join(", ", inactive)}.");

        // Someone assigned a form they can't open would only ever collect missed days.
        var allowedRoleNames = await _db.FormDefinitionRoles
            .Where(r => r.FormDefinitionId == form.Id)
            .Join(_db.Roles, fr => fr.RoleId, r => r.Id, (_, r) => r.Name)
            .ToListAsync(cancellationToken);
        var allowedUserIds = await _db.FormDefinitionUsers
            .Where(u => u.FormDefinitionId == form.Id)
            .Select(u => u.UserId)
            .ToListAsync(cancellationToken);
        var rolesByUser = (await _db.UserRoles
                .Where(ur => userIds.Contains(ur.UserId))
                .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.UserId, x => x.Name);
        var noAccess = users
            .Where(u => !FormAccessChecker.HasAccess(allowedRoleNames, allowedUserIds, rolesByUser[u.Id].ToList(), u.Id))
            .Select(u => u.DisplayName)
            .ToList();
        if (noAccess.Count > 0)
            throw new ValidationException(
                "assignment.noFormAccess", $"These users can't open '{form.Name}', so they can't fill it: {string.Join(", ", noAccess)}.");

        var newEnd = request.EndDate ?? DateOnly.MaxValue;
        var overlapping = await _db.FormAssignments
            .Where(a => a.FormDefinitionId == form.Id && userIds.Contains(a.UserId))
            .Where(a => a.StartDate <= newEnd && (a.EndDate == null || a.EndDate >= request.StartDate))
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var created = users
            .Where(u => !overlapping.Contains(u.Id))
            .Select(u => FormAssignment.Create(form.Id, u.Id, request.StartDate, request.EndDate, request.Note))
            .ToList();
        foreach (var assignment in created) _db.FormAssignments.Add(assignment);
        await _db.SaveChangesAsync(cancellationToken);

        // WhatsApp, best effort: a failed message is logged, the assignment stands.
        await _notifier.NotifyAssignedAsync(created.Select(a => a.Id).ToList(), cancellationToken);

        return new CreateFormAssignmentsResult(
            users.Count - overlapping.Count,
            users.Where(u => overlapping.Contains(u.Id)).Select(u => u.DisplayName).OrderBy(n => n).ToList());
    }
}
