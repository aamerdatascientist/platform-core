using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.UpdateFieldRules;

/// <summary>
/// Safe operation (same bucket as relabel/visibility): sets whether a field must be answered
/// and its value rules - Min/Max (with MinExclusive for "more than") on Number and Decimal,
/// a pattern on text, NoFutureDates on dates. Until this existed a field's IsRequired and
/// ValidationRulesJson could only be set when the field was first added, so a field created
/// optional by mistake (e.g. the Daily Progress Report's zone/footing/floor selectors) had no
/// way to become required short of deleting and re-adding it, losing its data.
///
/// Pure metadata, enforced by SubmissionValueValidator, apart from one piece of DDL: on a live
/// form, turning IsRequired OFF drops the column's NOT NULL (if it had one from first
/// publish), or the submissions that now leave it empty would fail in the database. Turning
/// it ON never adds NOT NULL - existing rows may hold nulls, and the validator is what
/// enforces it for new submissions.
/// </summary>
public record UpdateFieldRulesCommand(
    Guid FormDefinitionId,
    Guid FieldDefinitionId,
    bool IsRequired,
    double? Min,
    double? Max,
    bool MinExclusive,
    string? Regex,
    bool NoFutureDates,
    bool Unique = false,
    string? UniqueWithin = null) : IRequest;

public class UpdateFieldRulesCommandValidator : AbstractValidator<UpdateFieldRulesCommand>
{
    public UpdateFieldRulesCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
        RuleFor(x => x).Must(x => x.Min is null || x.Max is null || x.Min <= x.Max)
            .WithName("Min").WithMessage("The minimum can't be more than the maximum.");
        RuleFor(x => x.Regex).MaximumLength(500);
    }
}

public class UpdateFieldRulesCommandHandler : IRequestHandler<UpdateFieldRulesCommand>
{
    private static readonly JsonSerializerOptions RulesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IApplicationDbContext _db;
    private readonly IDynamicSchemaService _schemaService;

    public UpdateFieldRulesCommandHandler(IApplicationDbContext db, IDynamicSchemaService schemaService)
    {
        _db = db;
        _schemaService = schemaService;
    }

    public async Task Handle(UpdateFieldRulesCommand request, CancellationToken cancellationToken)
    {
        var formDefinition = await _db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken);

        if (formDefinition is null)
            throw new NotFoundException(nameof(Domain.Forms.FormDefinition), request.FormDefinitionId);

        var target = formDefinition.ResolveFieldEditTarget();
        var field = target.Version.FindFieldOrThrow(request.FieldDefinitionId);

        // Each rule is kept only where it means something for this field's type, so a stale
        // Min left over from a type change can't quietly apply to a text field.
        var isNumeric = field.FieldType is FieldType.Number or FieldType.Decimal;
        var isText = field.FieldType is FieldType.ShortText or FieldType.LongText;
        var isDate = field.FieldType == FieldType.DateTime;

        var regex = isText && !string.IsNullOrWhiteSpace(request.Regex) ? request.Regex.Trim() : null;
        if (regex is not null)
        {
            try { _ = new System.Text.RegularExpressions.Regex(regex); }
            catch (ArgumentException)
            {
                throw new Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure(nameof(request.Regex), "The pattern isn't a valid regular expression.")
                });
            }
        }

        // "Unique" is for ShortText only (codes and names); UniqueWithin has to name another
        // active field on the same version, since it's matched against the same submission.
        var unique = field.FieldType == FieldType.ShortText && request.Unique;
        var uniqueWithin = unique && !string.IsNullOrWhiteSpace(request.UniqueWithin) ? request.UniqueWithin.Trim() : null;
        if (uniqueWithin is not null &&
            (string.Equals(uniqueWithin, field.Code, StringComparison.OrdinalIgnoreCase) ||
             !target.Version.Fields.Any(f => f.IsActive && string.Equals(f.Code, uniqueWithin, StringComparison.OrdinalIgnoreCase))))
            throw new Common.Exceptions.ValidationException(new[]
            {
                new FluentValidation.Results.ValidationFailure(nameof(request.UniqueWithin),
                    $"'{uniqueWithin}' isn't another field on this form.")
            });

        var rules = new StoredRules(
            isNumeric ? request.Min : null,
            isNumeric ? request.Max : null,
            regex,
            isDate && request.NoFutureDates ? true : null,
            isNumeric && request.Min is not null && request.MinExclusive ? true : null,
            unique ? true : null,
            uniqueWithin);
        var hasAny = rules.Min is not null || rules.Max is not null || rules.Regex is not null ||
                     rules.NoFutureDates is not null || rules.Unique is not null;

        // DDL first (see the class summary): a nullable column under a still-required field
        // is harmless if the metadata save below then fails.
        if (target.IsLive && !request.IsRequired && field.IsRequired)
            await _schemaService.DropNotNullAsync(target.LiveTableName, field.Code, cancellationToken);

        field.SetRequired(request.IsRequired);
        field.SetValidationRules(hasAny ? JsonSerializer.Serialize(rules, RulesJson) : null);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private record StoredRules(
        double? Min, double? Max, string? Regex, bool? NoFutureDates, bool? MinExclusive, bool? Unique, string? UniqueWithin);
}
