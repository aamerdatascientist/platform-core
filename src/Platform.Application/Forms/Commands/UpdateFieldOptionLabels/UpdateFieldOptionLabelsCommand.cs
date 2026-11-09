using System.Text.Json;
using FluentValidation;
using MediatR;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms.Enums;

namespace Platform.Application.Forms.Commands.UpdateFieldOptionLabels;

public record OptionLabelDto(string Value, string Label, string? LabelEn);

/// <summary>
/// Safe (redesign §9.1): relabels a static Dropdown's options in Arabic and English, and may
/// reorder them. The set of option VALUES must stay exactly the same - values are what is
/// stored in the data table and what visibility conditions compare against, so changing one
/// here would orphan stored answers. Adding/removing options stays a type change.
/// </summary>
public record UpdateFieldOptionLabelsCommand(Guid FormDefinitionId, Guid FieldDefinitionId, IReadOnlyList<OptionLabelDto> Options) : IRequest;

public class UpdateFieldOptionLabelsCommandValidator : AbstractValidator<UpdateFieldOptionLabelsCommand>
{
    public UpdateFieldOptionLabelsCommandValidator()
    {
        RuleFor(x => x.FormDefinitionId).NotEmpty();
        RuleFor(x => x.FieldDefinitionId).NotEmpty();
        RuleFor(x => x.Options).NotEmpty();
        RuleForEach(x => x.Options).ChildRules(o =>
        {
            o.RuleFor(x => x.Value).NotEmpty();
            o.RuleFor(x => x.Label).NotEmpty().MaximumLength(200);
            o.RuleFor(x => x.LabelEn).MaximumLength(200);
        });
    }
}

public class UpdateFieldOptionLabelsCommandHandler : IRequestHandler<UpdateFieldOptionLabelsCommand>
{
    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions Write = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Arabic stays readable in the stored JSON, as the seed scripts wrote it.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private record StoredOption(string Value, string Label, string? LabelEn);

    private readonly IApplicationDbContext _db;
    public UpdateFieldOptionLabelsCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task Handle(UpdateFieldOptionLabelsCommand request, CancellationToken cancellationToken)
    {
        var form = await _db.LoadFormForEditAsync(request.FormDefinitionId, cancellationToken);
        var field = form.ResolveFieldEditTarget().Version.FindFieldOrThrow(request.FieldDefinitionId);

        if (field.FieldType != FieldType.Dropdown || field.DynamicOptionsSourceFormDefinitionId is not null || string.IsNullOrWhiteSpace(field.OptionsJson))
            throw FormEditLoading.Invalid(nameof(request.FieldDefinitionId), "Only a dropdown with its own list of options has option labels.");

        List<StoredOption> current;
        try { current = JsonSerializer.Deserialize<List<StoredOption>>(field.OptionsJson, Read) ?? new(); }
        catch (JsonException) { throw FormEditLoading.Invalid(nameof(request.Options), "This field's stored options can't be read."); }

        var sent = request.Options.Select(o => o.Value).ToList();
        if (sent.Count != sent.Distinct(StringComparer.Ordinal).Count()
            || !sent.ToHashSet(StringComparer.Ordinal).SetEquals(current.Select(o => o.Value)))
            throw FormEditLoading.Invalid(nameof(request.Options),
                "Send every existing option exactly once - option values can't be added, removed or changed here.");

        var next = request.Options.Select(o => new StoredOption(
            o.Value, o.Label.Trim(), string.IsNullOrWhiteSpace(o.LabelEn) ? null : o.LabelEn.Trim())).ToList();
        field.UpdateOptionLabels(JsonSerializer.Serialize(next, Write));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
