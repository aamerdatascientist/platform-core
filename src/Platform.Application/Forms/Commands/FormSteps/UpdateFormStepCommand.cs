using FluentValidation;
using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.FormSteps;

/// <summary>Safe (redesign §9.2): renames a step in Arabic and English.</summary>
public record UpdateFormStepCommand(Guid FormDefinitionId, Guid StepId, string TitleAr, string? TitleEn) : IRequest;

public class UpdateFormStepCommandValidator : AbstractValidator<UpdateFormStepCommand>
{
    public UpdateFormStepCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.StepId).NotEmpty();
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TitleEn).MaximumLength(200);
    }
}

public class UpdateFormStepCommandHandler : IRequestHandler<UpdateFormStepCommand>
{
    private readonly IApplicationDbContext _db;
    public UpdateFormStepCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(UpdateFormStepCommand request, CancellationToken cancellationToken)
    {
        var form = await _db.LoadFormForEditAsync(request.FormDefinitionId, cancellationToken);
        form.ResolveFieldEditTarget().Version.FindStepOrNotFound(request.StepId).Rename(request.TitleAr, request.TitleEn);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
