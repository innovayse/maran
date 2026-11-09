using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.CompleteFileDownload;

/// <summary>Records that a download's last byte was written to the response.</summary>
/// <remarks>
/// Dispatched by the download action itself, after the body has been written, and by nothing else: it
/// has no route. It exists as an operation of its own because the journal entry must be written by a
/// handler running in its own right, once the streaming handler that opened the file has returned and
/// is no longer part of the work. A transfer that dies before this is dispatched leaves the opening
/// entry and no completion, which is the honest record. Local bus only, like the query that opened it.
/// </remarks>
/// <param name="AccountId">The account whose file was sent.</param>
/// <param name="Path">The file that was sent, relative to the home.</param>
/// <param name="BytesSent">How many bytes were written to the response.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by
/// the action. Never bound from a request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and
/// stamped by the action. Never bound from a request.</param>
public sealed record CompleteFileDownloadCommand(
    Guid AccountId,
    string Path,
    ulong BytesSent,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
