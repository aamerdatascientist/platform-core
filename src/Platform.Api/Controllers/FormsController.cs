using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Forms.Commands.AddFieldDefinition;
using Platform.Application.Forms.Commands.CreateFormDefinition;
using Platform.Application.Forms.Commands.DeleteForm;
using Platform.Application.Forms.Commands.PublishFormVersion;
using Platform.Application.Forms.Commands.ArchiveField;
using Platform.Application.Forms.Commands.ChangeFieldType;
using Platform.Application.Forms.Commands.DeleteField;
using Platform.Application.Forms.Commands.RenameFieldCode;
using Platform.Application.Forms.Commands.ReorderFields;
using Platform.Application.Forms.Commands.RestoreField;
using Platform.Application.Forms.Commands.UpdateFieldLabel;
using Platform.Application.Forms.Commands.SetFormAllowedRoles;
using Platform.Application.Forms.Commands.SetFormAllowedUsers;
using Platform.Application.Forms.Commands.StartNewFormVersion;
using Platform.Application.Forms.Dtos;
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
        string? OptionsJson, Guid? LookupFormDefinitionId, string? ValidationRulesJson);

    [HttpPost("{id:guid}/fields")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> AddField(Guid id, AddFieldRequest request, CancellationToken cancellationToken)
    {
        var fieldId = await _sender.Send(new AddFieldDefinitionCommand(
            id, request.Code, request.Label, request.FieldType, request.IsRequired,
            request.OptionsJson, request.LookupFormDefinitionId, request.ValidationRulesJson), cancellationToken);

        return Ok(new { id = fieldId });
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<PublishFormVersionResult>> Publish(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new PublishFormVersionCommand(id), cancellationToken));

    public record UpdateFieldLabelRequest(string Label);

    /// <summary>Safe: metadata only, no column touched. Works on a published form with live data.</summary>
    [HttpPut("{id:guid}/fields/{fieldId:guid}/label")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> UpdateFieldLabel(
        Guid id, Guid fieldId, UpdateFieldLabelRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new UpdateFieldLabelCommand(id, fieldId, request.Label), cancellationToken);
        return NoContent();
    }

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
