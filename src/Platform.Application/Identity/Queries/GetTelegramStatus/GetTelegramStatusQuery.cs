using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;

namespace Platform.Application.Identity.Queries.GetTelegramStatus;

/// <param name="Configured">Whether the server has a bot at all (the Settings section hides itself if not).</param>
public record TelegramStatusDto(bool Configured, bool Linked, string? BotUsername);

public record GetTelegramStatusQuery(Guid UserId) : IRequest<TelegramStatusDto>;

public class GetTelegramStatusQueryHandler : IRequestHandler<GetTelegramStatusQuery, TelegramStatusDto>
{
    private readonly IApplicationDbContext _db;
    private readonly TelegramSettings _settings;

    public GetTelegramStatusQueryHandler(IApplicationDbContext db, TelegramSettings settings)
    {
        _db = db;
        _settings = settings;
    }

    public async Task<TelegramStatusDto> Handle(GetTelegramStatusQuery request, CancellationToken cancellationToken)
    {
        var linked = await _db.Users.Where(u => u.Id == request.UserId)
            .Select(u => (bool?)(u.TelegramChatId != null))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Platform.Domain.Identity.User), request.UserId);
        return new TelegramStatusDto(
            _settings.IsConfigured, linked, _settings.IsConfigured ? _settings.BotUsername!.TrimStart('@') : null);
    }
}
