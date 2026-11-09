using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Forms.Commands.AddFieldDefinition;
using Platform.Application.Forms.Commands.FormSections;
using Platform.Application.Forms.Commands.CreateFormDefinition;
using Platform.Application.Forms.Commands.DeleteForm;
using Platform.Application.Forms.Commands.PublishFormVersion;
using Platform.Application.Forms.Commands.ArchiveField;
using Platform.Application.Forms.Commands.ChangeFieldType;
using Platform.Application.Forms.Commands.DeleteField;
using Platform.Application.Forms.Commands.RenameFieldCode;
using Platform.Application.Forms.Commands.ReorderFields;
using Platform.Application.Forms.Commands.RestoreField;
using Platform.Application.Forms.Commands.UpdateFieldDynamicOptionsSource;
using Platform.Application.Forms.Commands.UpdateFieldLabel;
using Platform.Application.Forms.Commands.UpdateFieldOptionLabels;
using Platform.Application.Forms.Commands.UpdateFormName;
using Platform.Application.Forms.Commands.FormSteps;
using Platform.Application.Forms.Commands.UpdateFieldLookupFilter;
using Platform.Application.Forms.Commands.UpdateFieldRules;
using Platform.Application.Forms.Commands.UpdateFieldVisibility;
using Platform.Application.Forms.Commands.SetFormAllowedRoles;
using Platform.Application.Forms.Commands.SetFormAllowedUsers;
using Platform.Application.Forms.Commands.StartNewFormVersion;
using Platform.Application.Forms.Dtos;
using Platform.Application.Forms.Queries.GetFieldDynamicOptions;
using Platform.Application.Forms.Queries.GetLookupOptions;
using Platform.Application.Forms.Queries.GetFormDefinition;
using Platform.Application.Forms.Queries.GetFormsList;
using Platform.Domain.Forms.Enums;

namespace Platform.Api.Controllers;

/// <summary>
/// This is the API surface the form designer talks to.
///
/// While a form has never been published, every endpoint here is metadata-only - no dynamic
/// SQL runs until that first Publish, which is where all of its schema work happens at once.
/// Once a form IS published there's live data behind it and nothing useful to stage, so field
/// edits apply to the live version immediately and carry their own DDL: adding a field runs
/// ALTER TABLE ADD COLUMN there and then, renaming one runs RENAME COLUMN, and so on. Which
/// branch applies is decided in one place, FormEditTargetResolver.ResolveFieldEditTarget.
/// </summary>
[ApiController]
[Route("api/forms")]
[Authorize]
public class FormsController : ControllerBase
{
    private readonly ISender _sender;

    public FormsController(ISender sender) => _sender = sender;

    public record CreateFormRequest(string Code, string Name, string ModuleName, string? Description);

    [HttpPost]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Create(CreateFormRequest request, CancellationToken cancellationToken)
    {
        var id = await _sender.Send(
            new CreateFormDefinitionCommand(request.Code, request.Name, request.ModuleName, request.Description),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FormSummaryDto>>> List(
        [FromQuery] string? moduleName, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetFormsListQuery(moduleName), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FormDefinitionDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetFormDefinitionQuery(id), cancellationToken));

    public record AddFieldRequest(
        string Code, string Label, FieldType FieldType, bool IsRequired,
        string? OptionsJson, Guid? LookupFormDefinitionId, string? ValidationRulesJson,
        string? VisibleWhenFieldCode = null, string? VisibleWhenValuesJson = null,
        string? FilterByFieldCode = null, Guid? DynamicOptionsSourceFormDefinitionId = null,
        string? DynamicOptionsSourceFieldCode = null, string? LabelEn = null, Guid? StepId = null,
        Guid? SectionId = null, string? OptionsProvider = null, string? Formula = null, string? RowFilterJson = null,
        string? VisibleWhenAttribute = null);

    [HttpPost("{id:guid}/fields")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> AddField(Guid id, AddFieldRequest request, CancellationToken cancellationToken)
    {
        var fieldId = await _sender.Send(new AddFieldDefinitionCommand(
            id, request.Code, request.Label, request.FieldType, request.IsRequired,
            request.OptionsJson, request.LookupFormDefinitionId, request.ValidationRulesJson,
            request.VisibleWhenFieldCode, request.VisibleWhenValuesJson, request.FilterByFieldCode,
            request.DynamicOptionsSourceFormDefinitionId, request.DynamicOptionsSourceFieldCode,
            request.LabelEn, request.StepId, request.SectionId, request.OptionsProvider, request.Formula,
            request.RowFilterJson, request.VisibleWhenAttribute), cancellationToken);

        return Ok(new { id = fieldId });
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<PublishFormVersionResult>> Publish(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new PublishFormVersionCommand(id), cancellationToken));

    /// <summary>LabelEn: null leaves the English label unchanged, "" clears it.</summary>
    public record UpdateFieldLabelRequest(string Label, string? LabelEn = null);

    /// <summary>Safe: metadata only, no column touched. Works on a published form with live data.</summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/label")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateFieldLabel(
        Guid id, Guid fieldId, UpdateFieldLabelRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new UpdateFieldLabelCommand(id, fieldId, request.Label, request.LabelEn), cancellationToken);
        return NoContent();
    }

    // ---------------------------------------------------------------- redesign §9.1 / §9.2
    // All Safe: metadata only (no column, no data), so they work on a live published form.

    public record UpdateFormNameRequest(string Name, string? NameEn);

    [HttpPut("{id:guid}/name")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateName(Guid id, UpdateFormNameRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new UpdateFormNameCommand(id, request.Name, request.NameEn), cancellationToken);
        return NoContent();
    }

    public record UpdateFieldOptionLabelsRequest(IReadOnlyList<OptionLabelDto> Options);

    /// <summary>Relabels (and may reorder) a static dropdown's options; the set of values must not change.</summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/option-labels")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateFieldOptionLabels(
        Guid id, Guid fieldId, UpdateFieldOptionLabelsRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new UpdateFieldOptionLabelsCommand(id, fieldId, request.Options), cancellationToken);
        return NoContent();
    }

    public record FormStepRequest(string TitleAr, string? TitleEn);

    [HttpPost("{id:guid}/steps")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<FormStepDto>> AddStep(Guid id, FormStepRequest request, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new AddFormStepCommand(id, request.TitleAr, request.TitleEn), cancellationToken));

    [HttpPut("{id:guid}/steps/{stepId:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateStep(Guid id, Guid stepId, FormStepRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new UpdateFormStepCommand(id, stepId, request.TitleAr, request.TitleEn), cancellationToken);
        return NoContent();
    }

    public record ReorderStepsRequest(IReadOnlyList<Guid> StepIds);

    [HttpPut("{id:guid}/steps/order")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> ReorderSteps(Guid id, ReorderStepsRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new ReorderFormStepsCommand(id, request.StepIds), cancellationToken);
        return NoContent();
    }

    /// <summary>Its fields move to the previous step (the next one if it was first).</summary>
    [HttpDelete("{id:guid}/steps/{stepId:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> DeleteStep(Guid id, Guid stepId, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteFormStepCommand(id, stepId), cancellationToken);
        return NoContent();
    }

    public record SetFieldStepRequest(Guid? StepId);

    [HttpPut("{id:guid}/fields/{fieldId:guid}/step")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> SetFieldStep(Guid id, Guid fieldId, SetFieldStepRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new SetFieldStepCommand(id, fieldId, request.StepId), cancellationToken);
        return NoContent();
    }

    public record UpdateFieldVisibilityRequest(string? VisibleWhenFieldCode, string? VisibleWhenValuesJson);

    /// <summary>Safe: metadata only, no column touched. Works on a published form with live
    /// data. Send both fields null/empty to clear the condition.</summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/visibility")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateFieldVisibility(
        Guid id, Guid fieldId, UpdateFieldVisibilityRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(
            new UpdateFieldVisibilityCommand(id, fieldId, request.VisibleWhenFieldCode, request.VisibleWhenValuesJson),
            cancellationToken);
        return NoContent();
    }

    public record UpdateFieldRulesRequest(
        bool IsRequired, double? Min, double? Max, bool MinExclusive, string? Regex, bool NoFutureDates,
        bool Unique = false, string? UniqueWithin = null);

    /// <summary>Safe: sets "required" and the value rules (min/max, pattern, no future
    /// dates). Works on a published form with live data; enforced for new submissions.</summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/rules")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateFieldRules(
        Guid id, Guid fieldId, UpdateFieldRulesRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(
            new UpdateFieldRulesCommand(
                id, fieldId, request.IsRequired, request.Min, request.Max, request.MinExclusive, request.Regex,
                request.NoFutureDates, request.Unique, request.UniqueWithin),
            cancellationToken);
        return NoContent();
    }

    public record UpdateFieldLookupFilterRequest(string? FilterByFieldCode);

    /// <summary>Safe: metadata only, no column touched. Works on a published form with live
    /// data. Only meaningful on a Lookup field. Send FilterByFieldCode null/empty to clear
    /// the filter (the Lookup goes back to showing every target-form row).</summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/lookup-filter")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateFieldLookupFilter(
        Guid id, Guid fieldId, UpdateFieldLookupFilterRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(
            new UpdateFieldLookupFilterCommand(id, fieldId, request.FilterByFieldCode), cancellationToken);
        return NoContent();
    }

    public record UpdateFieldDynamicOptionsSourceRequest(Guid? SourceFormDefinitionId, string? SourceFieldCode);

    /// <summary>Safe: metadata only, no column touched. Works on a published form with live
    /// data. Only meaningful on a Dropdown field. Send both null/empty to clear the source
    /// (the Dropdown goes back to its own static OptionsJson).</summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/dynamic-options-source")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateFieldDynamicOptionsSource(
        Guid id, Guid fieldId, UpdateFieldDynamicOptionsSourceRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(
            new UpdateFieldDynamicOptionsSourceCommand(id, fieldId, request.SourceFormDefinitionId, request.SourceFieldCode),
            cancellationToken);
        return NoContent();
    }

    /// <summary>The live option list for a Dropdown field sourced dynamically from another
    /// form's field - see GetFieldDynamicOptionsQuery. Empty for a field with no dynamic
    /// source configured, rather than an error.</summary>
    [HttpGet("{id:guid}/fields/{fieldId:guid}/dynamic-options")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetFieldDynamicOptions(
        Guid id, Guid fieldId, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetFieldDynamicOptionsQuery(id, fieldId), cancellationToken));

    /// <summary>The choices for a Lookup field on this form (Id + display label only) - see
    /// GetLookupOptionsQuery. Checked against access to THIS form, not the Lookup's target,
    /// so a form can be filled in by people who can't open the master-data form it points at.
    /// filterValue is the value picked in the sibling field a cascading Lookup is narrowed by.</summary>
    /// <remarks>Since 2026-11-10 this also serves fields whose options come from a provider
    /// (any choice field), applies a Lookup's row filter, and returns each option's attributes
    /// that other fields' visibility depends on. <c>v=code:value</c> (repeatable) passes the
    /// form's current values a provider needs - e.g. the project and the stage of a block.</remarks>
    [HttpGet("{id:guid}/fields/{fieldId:guid}/lookup-options")]
    public async Task<ActionResult<IReadOnlyList<LookupOptionDto>>> GetLookupOptions(
        Guid id, Guid fieldId, [FromQuery] string? filterValue, [FromQuery(Name = "v")] string[]? values, CancellationToken cancellationToken)
    {
        var current = (values ?? [])
            .Select(v => v.Split(':', 2))
            .Where(p => p.Length == 2 && p[0].Length is > 0 and <= 63)
            .GroupBy(p => p[0])
            .ToDictionary(g => g.Key, g => (string?)g.Last()[1]);
        return Ok(await _sender.Send(new GetLookupOptionsQuery(id, fieldId, filterValue, current), cancellationToken));
    }

    // ------------------------------------------------------------ repeatable sections (2026-11-10)

    public record SectionRequest(string? Code, string TitleAr, string? TitleEn, int MinBlocks, int MaxBlocks);

    [HttpPost("{id:guid}/sections")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<FormSectionDto>> AddSection(Guid id, SectionRequest request, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new AddFormSectionCommand(id, request.Code ?? string.Empty, request.TitleAr, request.TitleEn, request.MinBlocks, request.MaxBlocks), cancellationToken));

    [HttpPut("{id:guid}/sections/{sectionId:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<FormSectionDto>> UpdateSection(Guid id, Guid sectionId, SectionRequest request, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new UpdateFormSectionCommand(id, sectionId, request.TitleAr, request.TitleEn, request.MinBlocks, request.MaxBlocks), cancellationToken));

    [HttpDelete("{id:guid}/sections/{sectionId:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> RemoveSection(Guid id, Guid sectionId, CancellationToken cancellationToken)
    {
        await _sender.Send(new RemoveFormSectionCommand(id, sectionId), cancellationToken);
        return NoContent();
    }

    public record MoveToSectionRequest(Guid? SectionId);

    [HttpPut("{id:guid}/fields/{fieldId:guid}/section")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> MoveFieldToSection(Guid id, Guid fieldId, MoveToSectionRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new MoveFieldToSectionCommand(id, fieldId, request.SectionId), cancellationToken);
        return NoContent();
    }

    public record FieldSettingsRequest(string? Formula, string? RowFilterJson, string? VisibleWhenAttribute, string? OptionsProvider);

    /// <summary>Safe: a field's formula, row filter, visibility attribute and options provider (blank clears each).</summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/settings")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateFieldSettings(Guid id, Guid fieldId, FieldSettingsRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new UpdateFieldSettingsCommand(id, fieldId, request.Formula, request.RowFilterJson, request.VisibleWhenAttribute, request.OptionsProvider), cancellationToken);
        return NoContent();
    }

    public record FormFlagsRequest(bool IsSystem, bool IsHidden);

    [HttpPut("{id:guid}/flags")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> SetFlags(Guid id, FormFlagsRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new SetFormFlagsCommand(id, request.IsSystem, request.IsHidden), cancellationToken);
        return NoContent();
    }

    [HttpGet("options-providers")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<IReadOnlyList<OptionsProviderDto>>> GetOptionsProviders(CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetOptionsProvidersQuery(), cancellationToken));

    public record ReorderFieldsRequest(IReadOnlyList<Guid> OrderedFieldIds);

    /// <summary>Safe: metadata only. Takes the full ordered list of active field Ids.</summary>
    [HttpPut("{id:guid}/fields/order")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> ReorderFields(
        Guid id, ReorderFieldsRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new ReorderFieldsCommand(id, request.OrderedFieldIds), cancellationToken);
        return NoContent();
    }

    public record RenameFieldCodeRequest(string NewCode);

    /// <summary>
    /// Risky: renames the physical column (ALTER TABLE ... RENAME COLUMN). Data is preserved,
    /// but anything outside this database referring to the old column name breaks.
    /// </summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/code")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> RenameFieldCode(
        Guid id, Guid fieldId, RenameFieldCodeRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new RenameFieldCodeCommand(id, fieldId, request.NewCode), cancellationToken);
        return NoContent();
    }

    public record ChangeFieldTypeRequest(
        FieldType NewFieldType, string? OptionsJson, Guid? LookupFormDefinitionId);

    /// <summary>
    /// Risky: changes the physical column's type. Refused outright (400, with the offending
    /// record Ids) if any existing value wouldn't survive the conversion.
    /// </summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/type")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> ChangeFieldType(
        Guid id, Guid fieldId, ChangeFieldTypeRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(
            new ChangeFieldTypeCommand(id, fieldId, request.NewFieldType, request.OptionsJson, request.LookupFormDefinitionId),
            cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// The recommended way to take a field off a form: hides it from the form and the
    /// reporting view, keeps the column and all historical data. Reversible via restore.
    /// </summary>
    [HttpPost("{id:guid}/fields/{fieldId:guid}/archive")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> ArchiveField(Guid id, Guid fieldId, CancellationToken cancellationToken)
    {
        await _sender.Send(new ArchiveFieldCommand(id, fieldId), cancellationToken);
        return NoContent();
    }

    /// <summary>Brings an archived field back, data intact.</summary>
    [HttpPost("{id:guid}/fields/{fieldId:guid}/restore")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> RestoreField(Guid id, Guid fieldId, CancellationToken cancellationToken)
    {
        await _sender.Send(new RestoreFieldCommand(id, fieldId), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Destructive and irreversible: DROPs the column and every value in it. On a published
    /// form, confirmFieldCode must equal the field's own Code. Archive instead unless the data
    /// genuinely should not exist. Deliberately not the DELETE verb on the bare field route -
    /// there is no longer any single "remove this field" operation that picks a behaviour for
    /// you, which is what the old endpoint here did.
    /// </summary>
    [HttpDelete("{id:guid}/fields/{fieldId:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> DeleteField(
        Guid id, Guid fieldId, [FromQuery] string? confirmFieldCode, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteFieldCommand(id, fieldId, confirmFieldCode), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/versions")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> StartNewVersion(Guid id, CancellationToken cancellationToken)
    {
        var newVersionId = await _sender.Send(new StartNewFormVersionCommand(id), cancellationToken);
        return Ok(new { id = newVersionId });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteFormCommand(id), cancellationToken);
        return NoContent();
    }

    public record SetAllowedRolesRequest(IReadOnlyList<Guid> RoleIds);

    [HttpPut("{id:guid}/allowed-roles")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> SetAllowedRoles(Guid id, SetAllowedRolesRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new SetFormAllowedRolesCommand(id, request.RoleIds), cancellationToken);
        return NoContent();
    }

    public record SetAllowedUsersRequest(IReadOnlyList<Guid> UserIds);

    [HttpPut("{id:guid}/allowed-users")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> SetAllowedUsers(Guid id, SetAllowedUsersRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new SetFormAllowedUsersCommand(id, request.UserIds), cancellationToken);
        return NoContent();
    }
}
