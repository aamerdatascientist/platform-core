using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Notifications;
using ValidationException = Platform.Application.Common.Exceptions.ValidationException;

namespace Platform.Application.Identity.Commands.CreateTelegramLink;

public record TelegramLinkDto(string Url, DateTime ExpiresAtUtc);

/// <summary>
/// A fresh "Connect Telegram" link for a user (valid <see cref="TelegramLinker.LinkLifetime"/>,
/// replaces any earlier one). Used by the user themself in Settings, or by an administrator to
/// send someone their link.
/// </summary>
public record CreateTelegramLinkCommand(Guid UserId) : IRequest<TelegramLinkDto>;

public class CreateTelegramLinkCommandHandler : IRequestHandler<CreateTelegramLinkCommand, TelegramLinkDto>
{
    private readonly IApplicationDbContext _db;
    private readonly TelegramSettings _settings;

    public CreateTelegramLinkCommandHandler(IApplicationDbContext db, TelegramSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<TelegramLinkDto> Handle(CreateTelegramLinkCommand request, CancellationToken cancellationToken)
    {
        if (!_settings.IsConfigured)
            throw new ValidationException("telegram.notConfigured", "Telegram isn't set up on this server.");

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(Platform.Domain.Identity.User), request.UserId);

        var code = TelegramLinker.NewCode();
        var expires = DateTime.UtcNow + TelegramLinker.LinkLifetime;
        user.StartTelegramLink(code, expires);
        await _db.SaveChangesAsync(cancellationToken);
        return new TelegramLinkDto(_settings.LinkFor(code), expires);
    }
}
