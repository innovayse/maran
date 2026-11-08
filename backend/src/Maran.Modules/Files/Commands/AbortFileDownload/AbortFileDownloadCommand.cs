using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.AbortFileDownload;

/// <summary>Records that a download ended before its last byte was written to the response.</summary>
/// <remarks>
/// The counterpart of <c>CompleteFileDownloadCommand</c>, dispatched by the download action itself after the
/// stream failed or the client went away, and by nothing else: it has no route. It closes the opening entry
/// with the outcome the transfer really had, so a download that died is not left looking like one still
/// running. Local bus only, like the query that opened the file.
/// </remarks>
/// <param name="AccountId">The account whose file was being sent.</param>
/// <param name="Path">The file that was being sent, relative to the home.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by
/// the action. Never bound from a request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and
/// stamped by the action. Never bound from a request.</param>
public sealed record AbortFileDownloadCommand(
    Guid AccountId,
    string Path,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
