using FluentValidation;
using MediatR;
using Platform.Application.Common.Interfaces;
using Platform.Application.Forms.Dtos;

namespace Platform.Application.Forms.Commands.FormSteps;

/// <summary>Safe (redesign §9.2): adds a step at the end of the form version being edited
/// (the live published one, or the draft of a never-published form).</summary>
public record AddFormStepCommand(Guid FormDefinitionId, string TitleAr, string? TitleEn) : IRequest<FormStepDto>;

public class AddFormStepCommandValidator : AbstractValidator<AddFormStepCommand>
{
    public AddFormStepCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TitleEn).MaximumLength(200);
    }
}

public class AddFormStepCommandHandler : IRequestHandler<AddFormStepCommand, FormStepDto>
{
    private readonly IApplicationDbContext _db;
    public AddFormStepCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<FormStepDto> Handle(AddFormStepCommand request, CancellationToken cancellationToken)
    {
        var form = await _db.LoadFormForEditAsync(request.FormDefinitionId, cancellationToken);
        var step = form.ResolveFieldEditTarget().Version.AddStep(request.TitleAr, request.TitleEn);
        // Added explicitly: a new child reached only through a tracked parent's collection is
        // silently not inserted (client-generated keys; CLAUDE.md engineering gotchas).
        _db.FormSteps.Add(step);
        await _db.SaveChangesAsync(cancellationToken);
        return new FormStepDto(step.Id, step.TitleAr, step.TitleEn, step.DisplayOrder);
    }
}
