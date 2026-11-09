using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Records;

public record MyEntriesPageDto(IReadOnlyList<RecordItemDto> Items, string? NextBefore);

/// <summary>
/// My entries (screen 08, HANDOFF §9.3): the caller's OWN non-deleted records, newest first,
/// across every published form they can access - for every role, PMs included. "Own" is
/// enforced here (CreatedByUserId = the signed-in user), never taken from the request. A formId
/// the caller can't access is a 403, not an empty list. Paged with a (sent-at, id) cursor:
/// "before" is the previous page's last item.
/// </summary>
public record GetMyEntriesQuery(Guid? FormId = null, string? Before = null, int PageSize = 20) : IRequest<MyEntriesPageDto>;

public class GetMyEntriesQueryHandler : IRequestHandler<GetMyEntriesQuery, MyEntriesPageDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDynamicDataRepository _repository;
    private readonly IFieldOptionsProviders _providers;
    private readonly ICurrentUserService _currentUser;

    public GetMyEntriesQueryHandler(IApplicationDbContext db, IDynamicDataRepository repository, ICurrentUserService currentUser, IFieldOptionsProviders providers)
    {
        _providers = providers;
        _db = db;
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<MyEntriesPageDto> Handle(GetMyEntriesQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException("Sign in first.");
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var before = ParseCursor(request.Before);

        var forms = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
            .Include(f => f.AllowedRoles).Include(f => f.AllowedUsers)
            .Where(f => f.Status == FormStatus.Published && f.TableName != null)
            .ToListAsync(cancellationToken);
        var roleNames = await _db.Roles.ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        bool Allowed(Domain.Forms.FormDefinition f) => FormAccessChecker.HasAccess(
            f.AllowedRoles.Select(r => roleNames.GetValueOrDefault(r.RoleId)).Where(n => n is not null).Select(n => n!).ToList(),
            f.AllowedUsers.Select(u => u.UserId).ToList(), _currentUser.Roles, userId);

        if (request.FormId is { } formId)
        {
            var only = forms.SingleOrDefault(f => f.Id == formId) ?? throw new NotFoundException("Form", formId);
            if (!Allowed(only)) throw new ForbiddenAccessException($"You don't have access to form '{only.Name}'.");
            forms = new() { only };
        }
        else forms = forms.Where(Allowed).ToList();

        // Newest pageSize+1 of each form, merged: enough to fill a page and know if there's more.
        var found = new List<(Domain.Forms.FormDefinition Form, RecordRow Row)>();
        foreach (var form in forms)
        {
            var version = form.GetPublishedVersion();
            if (version is null) continue;
            var fields = version.Fields.Where(f => f.IsActive).ToList();
            var rows = await _repository.SearchAsync(form.TableName!, fields,
                new RecordSearch(1, pageSize + 1, CreatedByUserId: userId, Before: before, CountTotal: false), cancellationToken);
            found.AddRange(rows.Items.Select(r => (form, r)));
        }
        var ordered = found.OrderByDescending(x => x.Row.CreatedAtUtc).ThenByDescending(x => x.Row.Id).ToList();
        var pageRows = ordered.Take(pageSize).ToList();

        var items = new List<RecordItemDto>();
        foreach (var group in pageRows.GroupBy(x => x.Form))
        {
            var fields = group.Key.GetPublishedVersion()!.Fields.Where(f => f.IsActive).ToList();
            items.AddRange(await RecordViews.ToItemsAsync(_db, _repository, group.Key.Id, fields, group.Select(x => x.Row).ToList(), cancellationToken, _providers));
        }
        items = items.OrderByDescending(i => i.CreatedAtUtc).ThenByDescending(i => i.Id).ToList();
        var next = ordered.Count > pageSize && pageRows.Count > 0
            ? $"{pageRows[^1].Row.CreatedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)}_{pageRows[^1].Row.Id}"
            : null;
        return new MyEntriesPageDto(items, next);
    }

    private static (DateTime, Guid)? ParseCursor(string? before)
    {
        if (string.IsNullOrWhiteSpace(before)) return null;
        var parts = before.Split('_');
        if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || !Guid.TryParse(parts[1], out var id)
            || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            throw FormEditLoading.Invalid(nameof(GetMyEntriesQuery.Before), "Unknown cursor.");
        return (new DateTime(ticks, DateTimeKind.Utc), id);
    }
}
