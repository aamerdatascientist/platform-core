using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.ReorderFields;

/// <summary>
/// Safe operation 2 of 3: sets DisplayOrder from an explicit ordered list of field Ids.
/// Purely how the form is laid out for whoever fills it in - no column, no value, and not
/// even the reporting view is affected (the view's column order comes from the same
/// DisplayOrder, but nothing downstream depends on view column ORDER the way it depends on
/// column names). So this runs with no confirmation on a published form.
/// </summary>
public record ReorderFieldsCommand(Guid FormDefinitionId, IReadOnlyList<Guid> OrderedFieldIds) : IRequest;

public class ReorderFieldsCommandValidator : AbstractValidator<ReorderFieldsCommand>
{
    public ReorderFieldsCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.OrderedFieldIds).NotEmpty();
        RuleFor(x => x.OrderedFieldIds)
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .When(x => x.OrderedFieldIds is not null)
            .WithMessage("The same field cannot appear twice in the order.");
    }
}

public class ReorderFieldsCommandHandler : IRequestHandler<ReorderFieldsCommand>
{
    private readonly IApplicationDbContext _db;

    public ReorderFieldsCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(ReorderFieldsCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();

        try
        {
            target.Version.ReorderFields(request.OrderedFieldIds);
        }
        catch (InvalidOperationException ex)
        {
            // "You sent a partial list" is a caller mistake, not a server fault - most likely
            // a stale form open in one tab while a field was added or archived in another.
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.OrderedFieldIds), ex.Message)
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
