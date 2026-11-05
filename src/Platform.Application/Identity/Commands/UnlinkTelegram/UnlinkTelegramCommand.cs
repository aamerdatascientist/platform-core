using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Commands.UnlinkTelegram;

/// <summary>Stops Telegram messages to a user and cancels any open link.</summary>
public record UnlinkTelegramCommand(Guid UserId) : IRequest;

public class UnlinkTelegramCommandHandler : IRequestHandler<UnlinkTelegramCommand>
{
    private readonly IApplicationDbContext _db;

    public UnlinkTelegramCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(UnlinkTelegramCommand request, CancellationToken cancellationToken)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException(nameof(Platform.Domain.Identity.User), request.UserId);
        user.UnlinkTelegram();
        await _db.SaveChangesAsync(cancellationToken);
    }
}
