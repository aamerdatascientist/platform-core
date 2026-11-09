using FluentValidation;
using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.FormSteps;

/// <summary>
/// Safe (redesign §9.2): deletes a step. No field is lost - its fields move to the previous
/// step (to the next one when it was the first, or become step-less when it was the only one).
/// </summary>
public record DeleteFormStepCommand(Guid FormDefinitionId, Guid StepId) : IRequest;

public class DeleteFormStepCommandValidator : AbstractValidator<DeleteFormStepCommand>
{
    public DeleteFormStepCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.StepId).NotEmpty();
    }
}

public class DeleteFormStepCommandHandler : IRequestHandler<DeleteFormStepCommand>
{
    private readonly IApplicationDbContext _db;
    public DeleteFormStepCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(DeleteFormStepCommand request, CancellationToken cancellationToken)
    {
        var form = await _db.LoadFormForEditAsync(request.FormDefinitionId, cancellationToken);
        var version = form.ResolveFieldEditTarget().Version;
        version.FindStepOrNotFound(request.StepId);
        var removed = version.RemoveStep(request.StepId);
        _db.FormSteps.Remove(removed);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
