using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Notifications;
using ValidationException = Platform.Application.Common.Exceptions.ValidationException;

namespace Platform.Application.Identity.Commands.SetUserPhoneNumber;

/// <summary>Sets (or, blank, removes) a user's mobile number for WhatsApp notifications.</summary>
public record SetUserPhoneNumberCommand(Guid UserId, string? PhoneNumber) : IRequest;

public class SetUserPhoneNumberCommandValidator : AbstractValidator<SetUserPhoneNumberCommand>
{
    public SetUserPhoneNumberCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
    }
}

public class SetUserPhoneNumberCommandHandler : IRequestHandler<SetUserPhoneNumberCommand>
{
    private readonly IApplicationDbContext _db;

    public SetUserPhoneNumberCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(SetUserPhoneNumberCommand request, CancellationToken cancellationToken)
    {
        string? phone;
        try
        {
            phone = PhoneNumbers.Normalize(request.PhoneNumber);
        }
        catch (FormatException ex)
        {
            throw new ValidationException("user.phoneInvalid", ex.Message);
        }

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(Platform.Domain.Identity.User), request.UserId);
        user.SetPhoneNumber(phone);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
