using Maran.Modules.Files.Commands.AbortFileDownload;
using Maran.Modules.Files.Commands.CompleteFileDownload;
using Maran.Modules.Files.Commands.SaveFileText;
using Maran.Modules.Files.Commands.UploadFile;
using Maran.Modules.Files.Common;
using Maran.Modules.Files.Models;
using Maran.Modules.Files.Options;
using Maran.Modules.Files.Queries.DownloadFile;
using Maran.Sdk.Contracts;
using Maran.Sdk.Controllers;
using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Maran.Modules.Files.Controllers;

/// <summary>
/// HTTP surface for the file manager's routes that move a customer's bytes: saving an edited text file,
/// uploading a file as a raw body and downloading one as a stream. Thin by design (rules/csharp.md
/// "Controller shape is fixed"), except for the one thing that is the HTTP surface itself and has to be
/// done here — the request body limit and the response headers and body of a stream.
/// </summary>
/// <remarks>
/// <para>
/// Every route here copies a customer's data in or out or changes it, so an operator viewing as a
/// customer is refused all three: looking is not copying (rules/csharp.md "view-as").
/// </para>
/// <para>
/// <b>The upload is a raw <c>application/octet-stream</c> body, never a multipart form</b>, with the
/// path in the query. A form is spooled to disk by the panel process, which runs unprivileged and would
/// hold a customer's bytes outside their home. The body limit is set from configuration inside the
/// action, because the value is not a constant an attribute can carry; the reverse proxy has its own
/// ceiling for this route, rendered from the same setting (installer/lib/80-nginx.sh).
/// </para>
/// <para>
/// <b>The download never reaches the browser as anything but an attachment</b>
/// (<see cref="FilesTransferResponse"/>). A refusal is answered before any header of the download is set.
/// </para>
/// </remarks>
[Route("api/v1/files")]
[Authorize(Policy = AuthorizationPolicies.AnyAuthenticated)]
[Tags("Files")]
[Produces("application/json")]
[EnableRateLimiting(RateLimitPolicies.Files)]
[RequiresAccountFeature(AccountFeature.Files)]
public sealed class FilesTransferController : BaseApiController
{
    /// <summary>The message bus commands and queries are dispatched through.</summary>
    private readonly IMessageBus _bus;

    /// <summary>The limits, read at each request so a reloaded setting takes effect.</summary>
    private readonly IOptionsMonitor<FilesOptions> _options;

    /// <summary>Creates the controller with the caller identity, the message bus and the limits.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="bus">The message bus commands and queries are dispatched through.</param>
    /// <param name="options">The limits.</param>
    public FilesTransferController(ICurrentUser currentUser, IMessageBus bus, IOptionsMonitor<FilesOptions> options)
        : base(currentUser)
    {
        _bus = bus;
        _options = options;
    }

    /// <summary>
    /// Replaces a file's text with the editor's content, keeping its permissions. Another customer's
    /// account answers 404, not 403.
    /// </summary>
    /// <param name="command">The account, the file, the new text and the modification time it was loaded at.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    [HttpPut("content")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileSavedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveContentAsync(
        [FromBody] SaveFileTextCommand command,
        CancellationToken cancellationToken)
    {
        command = command with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        return ToActionResult(await _bus.InvokeAsync<Result<FileSavedDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Writes the request body to a file, as it arrives. Another customer's account answers 404, not 403.
    /// </summary>
    /// <param name="command">The account, the destination path and whether an existing file may be replaced.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects, which discards the partial file.</param>
    [HttpPost("upload")]
    [Consumes("application/octet-stream")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(typeof(FileUploadedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UploadAsync(
        [FromQuery] UploadFileCommand command,
        CancellationToken cancellationToken)
    {
        // One byte past the cap, so that a body of exactly the cap is accepted and the first byte beyond
        // it is counted and refused by the handler with a readable answer instead of by the server's
        // own limit with a bare one. The limit cannot be changed once the body has started to be read,
        // which is why it is set before anything else here.
        var limit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
        {
            limit.MaxRequestBodySize = _options.CurrentValue.MaxUploadBytes + 1;
        }

        command = command with
        {
            Body = Request.Body,
            DeclaredBytes = Request.ContentLength,
            IpAddress = ClientIpAddress,
            UserAgent = CallerUserAgent,
        };

        return ToActionResult(await _bus.InvokeAsync<Result<FileUploadedDto>>(command, cancellationToken));
    }

    /// <summary>
    /// Streams a file as an attachment. A refusal is a problem document with no download headers. Another
    /// customer's account answers 404, not 403.
    /// </summary>
    /// <param name="query">The account and the file's path.</param>
    /// <param name="cancellationToken">Cancelled when the client disconnects.</param>
    [HttpGet("download")]
    [Produces(FilesTransferResponse.DownloadContentType, "application/json")]
    [ViewAsAccess(ViewAsAccess.Refused)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> DownloadAsync(
        [FromQuery] DownloadFileQuery query,
        CancellationToken cancellationToken)
    {
        query = query with { IpAddress = ClientIpAddress, UserAgent = CallerUserAgent };

        var opened = await _bus.InvokeAsync<Result<FileDownload>>(query, cancellationToken);
        if (!opened.IsSuccess)
        {
            return ToActionResult(opened);
        }

        await using var download = opened.Value;
        FilesTransferResponse.PrepareDownload(Response, download);

        try
        {
            await download.CopyToAsync(Response.Body, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // The client went away, or the agent ended the stream short. The response has declared a length it
            // will not reach, so the connection is closed instead of finished, and the journal says the download
            // did not complete. The connection is closed first so that a failure to journal cannot leave it open.
            HttpContext.Abort();
            await _bus.InvokeAsync<Result>(
                new AbortFileDownloadCommand(query.AccountId, query.Path, ClientIpAddress, CallerUserAgent),
                CancellationToken.None);
            return new EmptyResult();
        }

        await _bus.InvokeAsync<Result>(
            new CompleteFileDownloadCommand(
                query.AccountId, query.Path, download.SizeBytes, ClientIpAddress, CallerUserAgent),
            CancellationToken.None);

        return new EmptyResult();
    }
}
