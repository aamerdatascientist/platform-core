using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Forms.Commands.AddFieldDefinition;
using Platform.Application.Forms.Commands.CreateFormDefinition;
using Platform.Application.Forms.Commands.PublishFormVersion;
using Platform.Application.Forms.Dtos;
using Platform.Application.Forms.Queries.GetFormDefinition;
using Platform.Domain.Forms.Enums;

namespace Platform.Api.Controllers;

/// <summary>
/// This is the API surface the (future) drag-and-drop form designer talks to. Every
/// endpoint here is metadata-only until Publish - no dynamic SQL runs before that point.
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
    public async Task<IActionResult> Create(CreateFormRequest request, CancellationToken cancellationToken)
    {
        var id = await _sender.Send(
            new CreateFormDefinitionCommand(request.Code, request.Name, request.ModuleName, request.Description),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FormDefinitionDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetFormDefinitionQuery(id), cancellationToken));

    public record AddFieldRequest(
        string Code, string Label, FieldType FieldType, bool IsRequired,
        string? OptionsJson, Guid? LookupFormDefinitionId, string? ValidationRulesJson);

    [HttpPost("{id:guid}/fields")]
    public async Task<IActionResult> AddField(Guid id, AddFieldRequest request, CancellationToken cancellationToken)
    {
        var fieldId = await _sender.Send(new AddFieldDefinitionCommand(
            id, request.Code, request.Label, request.FieldType, request.IsRequired,
            request.OptionsJson, request.LookupFormDefinitionId, request.ValidationRulesJson), cancellationToken);

        return Ok(new { id = fieldId });
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<PublishFormVersionResult>> Publish(Guid id, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new PublishFormVersionCommand(id), cancellationToken));
}
