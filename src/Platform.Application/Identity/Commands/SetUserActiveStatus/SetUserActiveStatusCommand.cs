using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Commands.SetUserActiveStatus;

public record SetUserActiveStatusCommand(Guid UserId, bool IsActive) : IRequest;

public class SetUserActiveStatusCommandValidator : AbstractValidator<SetUserActiveStatusCommand>
{
    public SetUserActiveStatusCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public class SetUserActiveStatusCommandHandler : IRequestHandler<SetUserActiveStatusCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetUserActiveStatusCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task Handle(SetUserActiveStatusCommand request, CancellationToken cancellationToken)
    {
        if (!request.IsActive && request.UserId == _currentUser.UserId)
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.UserId), "You can't deactivate your own account.")
            });

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
            throw new NotFoundException(nameof(Platform.Domain.Identity.User), request.UserId);

        if (request.IsActive) user.Activate();
        else user.Deactivate();

        await _db.SaveChangesAsync(cancellationToken);
    }
}
