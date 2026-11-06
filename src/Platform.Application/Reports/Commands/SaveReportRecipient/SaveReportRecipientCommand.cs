using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Application.Notifications;
using Platform.Domain.Reports;
using ValidationException = Platform.Application.Common.Exceptions.ValidationException;

namespace Platform.Application.Reports.Commands.SaveReportRecipient;

/// <summary>Adds a report recipient (no Id) or updates one.</summary>
public record SaveReportRecipientCommand(
    Guid? Id, string Name, string PhoneNumber, bool ReceivesDaily, bool ReceivesWeekly, bool ReceivesMonthly, bool IsActive)
    : IRequest<ReportRecipientDto>;

public class SaveReportRecipientCommandValidator : AbstractValidator<SaveReportRecipientCommand>
{
    public SaveReportRecipientCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30);
    }
}

public class SaveReportRecipientCommandHandler : IRequestHandler<SaveReportRecipientCommand, ReportRecipientDto>
{
    private readonly IApplicationDbContext _db;

    public SaveReportRecipientCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<ReportRecipientDto> Handle(SaveReportRecipientCommand request, CancellationToken cancellationToken)
    {
        string phone;
        try
        {
            phone = PhoneNumbers.Normalize(request.PhoneNumber)!;
        }
        catch (FormatException ex)
        {
            throw new ValidationException("user.phoneInvalid", ex.Message);
        }

        ReportRecipient recipient;
        if (request.Id is { } id)
        {
            recipient = await _db.ReportRecipients.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(ReportRecipient), id);
            recipient.Update(request.Name, phone, request.ReceivesDaily, request.ReceivesWeekly, request.ReceivesMonthly, request.IsActive);
        }
        else
        {
            recipient = ReportRecipient.Create(request.Name, phone, request.ReceivesDaily, request.ReceivesWeekly, request.ReceivesMonthly);
            _db.ReportRecipients.Add(recipient);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ReportRecipientDto.From(recipient);
    }
}
