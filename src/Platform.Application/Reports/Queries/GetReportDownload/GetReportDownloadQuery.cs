using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Reports;

namespace Platform.Application.Reports.Queries.GetReportDownload;

/// <summary>A short-lived download link for a stored report PDF (A4 or phone).</summary>
public record GetReportDownloadQuery(Guid RunId, bool Phone) : IRequest<string>;

public class GetReportDownloadQueryHandler : IRequestHandler<GetReportDownloadQuery, string>
{
    private readonly IApplicationDbContext _db;
    private readonly IBlobStorageService _blobs;

    public GetReportDownloadQueryHandler(IApplicationDbContext db, IBlobStorageService blobs)
    {
        _db = db;
        _blobs = blobs;
    }

    public async Task<string> Handle(GetReportDownloadQuery request, CancellationToken cancellationToken)
    {
        var run = await _db.ReportRuns.SingleOrDefaultAsync(r => r.Id == request.RunId, cancellationToken)
            ?? throw new NotFoundException(nameof(ReportRun), request.RunId);
        var blob = (request.Phone ? run.PhonePdfBlobName : run.PdfBlobName)
            ?? throw new NotFoundException("Report PDF", request.RunId);
        return await _blobs.GetReadUrlAsync(blob, TimeSpan.FromMinutes(10), cancellationToken);
    }
}
