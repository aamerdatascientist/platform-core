using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.DeleteForm;

public record DeleteFormCommand(Guid FormDefinitionId) : IRequest;

public class DeleteFormCommandValidator : AbstractValidator<DeleteFormCommand>
{
    public DeleteFormCommandValidator() => RuleFor(x => x.FormDefinitionId).NotEmpty();
}

public class DeleteFormCommandHandler : IRequestHandler<DeleteFormCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteFormCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(DeleteFormCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        // Neither Lookup fields nor WorkflowDefinition have a real foreign-key
        // relationship to FormDefinitions - deliberate, to keep those modules decoupled -
        // which means nothing at the database level stops a delete from orphaning them.
        // Checked explicitly here instead, in three simple sequential queries rather than
        // one clever join, since this is an infrequent admin action where readability
        // matters more than round-trip count.
        var lookupFieldVersionIds = await _db.FieldDefinitions
            .Where(fd => fd.LookupFormDefinitionId == request.FormDefinitionId && fd.IsActive)
            .Select(fd => fd.FormVersionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (lookupFieldVersionIds.Count > 0)
        {
            var owningFormIds = await _db.FormVersions
                .Where(fv => lookupFieldVersionIds.Contains(fv.Id))
                .Select(fv => fv.FormDefinitionId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var referencingFormNames = await _db.FormDefinitions
                .Where(f => owningFormIds.Contains(f.Id))
                .Select(f => f.Name)
                .Distinct()
                .ToListAsync(cancellationToken);

            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.FormDefinitionId),
                    $"Can't delete - referenced by a Lookup field on: {string.Join(", ", referencingFormNames)}. Remove that field first.")
            });
        }

        var hasWorkflow = await _db.WorkflowDefinitions
            .AnyAsync(w => w.FormDefinitionId == request.FormDefinitionId, cancellationToken);

        if (hasWorkflow)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.FormDefinitionId),
                    "Can't delete - a workflow is attached to this form.")
            });

        if (formDefinition.TableName is null)
        {
            // Never published - no physical table, no submitted data. Safe to remove
            // outright; there's nothing meaningful to preserve.
            _db.FormDefinitions.Remove(formDefinition);
        }
        else
        {
            // Published at least once - real data may exist in the physical table.
            // Soft-delete only: the AuditableEntity query filter hides this from every
            // query automatically from here on, but the table and its data are left
            // physically untouched - consistent with this codebase never destructively
            // dropping schema (see DynamicSchemaService remarks). Recoverable at the
            // database level later if that's ever needed; no "undelete" UI exists yet.
            formDefinition.IsDeleted = true;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
