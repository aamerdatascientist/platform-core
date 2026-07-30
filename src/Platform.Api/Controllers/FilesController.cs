using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Application.Common.Interfaces;
using Platform.Application.Files.Commands.DeleteFile;
using Platform.Application.Files.Commands.UploadFile;
using Platform.Application.Files.Queries.GetFileDownloadUrl;
using Platform.Application.Files.Queries.GetFilesForRecord;

namespace Platform.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICurrentUserService _currentUser;

    public FilesController(ISender sender, ICurrentUserService currentUser)
    {
        _sender = sender;
        _currentUser = currentUser;
    }

    // Slightly above UploadFileCommandValidator's 20MB cap, so a too-large file gets that
    // validator's clean, specific error message rather than Kestrel's generic request-size
    // rejection before the request body is even fully read.
    [HttpPost("forms/{formId:guid}/records/{recordId:guid}/attachments")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<FileMetadataDto>> Upload(
        Guid formId, Guid recordId, [FromForm] string fieldCode, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var result = await _sender.Send(
            new UploadFileCommand(formId, recordId, fieldCode, stream, file.FileName, file.ContentType, file.Length, _currentUser.UserId!.Value),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("records/{recordId:guid}/attachments")]
    public async Task<ActionResult<IReadOnlyList<FileMetadataDto>>> ListForRecord(Guid recordId, CancellationToken cancellationToken) =>
        Ok(await _sender.Send(new GetFilesForRecordQuery(recordId), cancellationToken));

    [HttpGet("attachments/{fileId:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl(Guid fileId, CancellationToken cancellationToken)
    {
        var url = await _sender.Send(new GetFileDownloadUrlQuery(fileId), cancellationToken);
        return Ok(new { url });
    }

    [HttpDelete("attachments/{fileId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken cancellationToken)
    {
        await _sender.Send(new DeleteFileCommand(fileId), cancellationToken);
        return NoContent();
    }
}
