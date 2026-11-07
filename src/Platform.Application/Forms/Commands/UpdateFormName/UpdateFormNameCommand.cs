using FluentValidation;
using MediatR;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Forms.Commands.UpdateFormName;

/// <summary>
/// Safe (redesign §9.1): sets the form's Arabic name and its English name. Metadata only -
/// the physical table name was fixed at first publish and never follows Name.
/// </summary>
public record UpdateFormNameCommand(Guid FormDefinitionId, string Name, string? NameEn) : IRequest;

public class UpdateFormNameCommandValidator : AbstractValidator<UpdateFormNameCommand>
{
    public UpdateFormNameCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameEn).MaximumLength(200);
    }
}

public class UpdateFormNameCommandHandler : IRequestHandler<UpdateFormNameCommand>
{
    private readonly IApplicationDbContext _db;
    public UpdateFormNameCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(UpdateFormNameCommand request, CancellationToken cancellationToken)
    {
        var form = await _db.LoadFormForEditAsync(request.FormDefinitionId, cancellationToken);
        form.Rename(request.Name, request.NameEn);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
