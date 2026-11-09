using FluentValidation;
using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.FormSteps;

/// <summary>Safe (redesign §9.2): moves a field to a step of the same version (null = the
/// first step). The field's place inside the step is its DisplayOrder (PUT fields/order).</summary>
public record SetFieldStepCommand(Guid FormDefinitionId, Guid FieldDefinitionId, Guid? StepId) : IRequest;

public class SetFieldStepCommandValidator : AbstractValidator<SetFieldStepCommand>
{
    public SetFieldStepCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
    }
}

public class SetFieldStepCommandHandler : IRequestHandler<SetFieldStepCommand>
{
    private readonly IApplicationDbContext _db;
    public SetFieldStepCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(SetFieldStepCommand request, CancellationToken cancellationToken)
    {
        var form = await _db.LoadFormForEditAsync(request.FormDefinitionId, cancellationToken);
        var version = form.ResolveFieldEditTarget().Version;
        version.FindFieldOrThrow(request.FieldDefinitionId);
        if (request.StepId is { } stepId) version.FindStepOrNotFound(stepId);
        version.MoveFieldToStep(request.FieldDefinitionId, request.StepId);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
