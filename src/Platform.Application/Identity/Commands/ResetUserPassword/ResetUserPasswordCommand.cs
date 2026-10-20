using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Commands.ResetUserPassword;

/// <summary>
/// An administrator sets a new password for someone who forgot theirs (administrators only -
/// enforced on the endpoint). Signs that user out everywhere: every refresh token they hold
/// is revoked. The administrator passes the new password on to them, and they can change it
/// themselves afterwards.
/// </summary>
public record ResetUserPasswordCommand(Guid UserId, string NewPassword) : IRequest;

public class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.NewPassword).ValidPassword();
    }
}

public class ResetUserPasswordCommandHandler : IRequestHandler<ResetUserPasswordCommand>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    public ResetUserPasswordCommandHandler(IApplicationDbContext db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.UserId && !u.IsDeleted, cancellationToken)
            ?? throw new NotFoundException(nameof(Platform.Domain.Identity.User), request.UserId);

        user.ChangePasswordHash(_passwordHasher.Hash(request.NewPassword));
        await RefreshTokenRevocation.RevokeAllAsync(_db, user.Id, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
