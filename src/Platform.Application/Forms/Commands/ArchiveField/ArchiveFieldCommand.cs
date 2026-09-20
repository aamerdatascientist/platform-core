using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.ArchiveField;

/// <summary>
/// Risky operation 3 of 3, safe half: the recommended way to take a field off a form.
///
/// The field stops appearing on the form and stops accepting new submissions, but its column
/// and every historical value stay exactly where they are - so past records keep meaning what
/// they meant, and anything already reporting on them keeps working against the raw table.
/// The only visible loss is that the column drops out of the reporting view, since the view
/// is built from active fields.
///
/// Reversible: <see cref="RestoreFieldCommand"/> brings it back, data intact, because nothing
/// was ever destroyed. Contrast DeleteFieldCommand, which is not reversible at all.
/// </summary>
public record ArchiveFieldCommand(Guid FormDefinitionId, Guid FieldDefinitionId) : IRequest;

public class ArchiveFieldCommandValidator : AbstractValidator<ArchiveFieldCommand>
{
    public ArchiveFieldCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
    }
}

public class ArchiveFieldCommandHandler : IRequestHandler<ArchiveFieldCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicSchemaService _schemaService;

    public ArchiveFieldCommandHandler(IApplicationDbContext db, IDynamicSchemaService schemaService)
    {
        _db = db;
        _schemaService = schemaService;
    }

    public async Task Handle(ArchiveFieldCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);

        if (!field.IsActive) return;

        if (target.Version.Fields.Count(f => f.IsActive) == 1)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.FieldDefinitionId),
                    "This is the form's only remaining active field - a form with no fields can't be submitted " +
                    "or published. Add another field first, or delete the whole form.")
            });

        field.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);

        if (target.IsLive)
        {
            var lookupTargets = await _db.LoadLookupTargetsAsync(target.Version, cancellationToken);
            await _schemaService.RefreshReportingViewAsync(
                formDefinition, target.Version, lookupTargets, cancellationToken);
        }
    }
}
