using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Workflow;

namespace Platform.Application.Workflow.Commands.CreateWorkflowDefinition;

public record CreateWorkflowDefinitionCommand(string Code, string Name, Guid FormDefinitionId) : IRequest<Guid>;

public class CreateWorkflowDefinitionCommandValidator : AbstractValidator<CreateWorkflowDefinitionCommand>
{
    public CreateWorkflowDefinitionCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FormDefinitionId).NotEmpty();
    }
}

public class CreateWorkflowDefinitionCommandHandler : IRequestHandler<CreateWorkflowDefinitionCommand, Guid>
{
    private readonly IApplicationDbContext _db;

    public CreateWorkflowDefinitionCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Guid> Handle(CreateWorkflowDefinitionCommand request, CancellationToken cancellationToken)
    {
        var formExists = await _db.FormDefinitions.AnyAsync(f => f.Id == request.FormDefinitionId, cancellationToken);
        if (!formExists)
            throw new Common.Exceptions.NotFoundException(nameof(Platform.Domain.Forms.FormDefinition), request.FormDefinitionId);

        var codeTaken = await _db.WorkflowDefinitions.AnyAsync(w => w.Code == request.Code, cancellationToken);
        if (codeTaken)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(
                    nameof(request.Code), $"A workflow with code '{request.Code}' already exists.")
            });

        var workflow = Platform.Domain.Workflow.WorkflowDefinition.Create(request.Code, request.Name, request.FormDefinitionId);
        _db.WorkflowDefinitions.Add(workflow);
        await _db.SaveChangesAsync(cancellationToken);

        return workflow.Id;
    }
}
