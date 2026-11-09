using FluentValidation;
using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.FormSteps;

/// <summary>Safe (redesign §9.2): puts the steps in this order - every step exactly once.</summary>
public record ReorderFormStepsCommand(Guid FormDefinitionId, IReadOnlyList<Guid> StepIds) : IRequest;

public class ReorderFormStepsCommandValidator : AbstractValidator<ReorderFormStepsCommand>
{
    public ReorderFormStepsCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.StepIds).NotNull();
    }
}

public class ReorderFormStepsCommandHandler : IRequestHandler<ReorderFormStepsCommand>
{
    private readonly IApplicationDbContext _db;
    public ReorderFormStepsCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(ReorderFormStepsCommand request, CancellationToken cancellationToken)
    {
        var form = await _db.LoadFormForEditAsync(request.FormDefinitionId, cancellationToken);
        try
        {
            form.ResolveFieldEditTarget().Version.ReorderSteps(request.StepIds);
        }
        catch (InvalidOperationException ex)
        {
            throw FormEditLoading.Invalid(nameof(request.StepIds), ex.Message);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }
}
