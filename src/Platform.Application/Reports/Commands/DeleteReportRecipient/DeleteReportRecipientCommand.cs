using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Reports;

namespace Platform.Application.Reports.Commands.DeleteReportRecipient;

/// <summary>Removes a recipient (soft delete - past deliveries keep their history).</summary>
public record DeleteReportRecipientCommand(Guid Id) : IRequest;

public class DeleteReportRecipientCommandHandler : IRequestHandler<DeleteReportRecipientCommand>
{
    private readonly IApplicationDbContext _db;

    public DeleteReportRecipientCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(DeleteReportRecipientCommand request, CancellationToken cancellationToken)
    {
        var recipient = await _db.ReportRecipients.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(ReportRecipient), request.Id);
        recipient.IsDeleted = true;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
