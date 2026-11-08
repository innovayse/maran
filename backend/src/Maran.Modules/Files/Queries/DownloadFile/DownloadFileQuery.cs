using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Queries.DownloadFile;

/// <summary>Opens one file of an account's home to be streamed to the caller as a download.</summary>
/// <remarks>
/// The answer is a <c>FileDownload</c>, which holds a live agent stream and a transfer slot and is
/// meaningful on the local bus only: dispatched through a durable or external queue it could not be
/// carried. A folder is downloaded by archiving it first and downloading the archive.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Path">The file, relative to the home. Never empty.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by
/// the action. Never bound from the request; the download is journalled under it.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and
/// stamped by the action. Never bound from the request.</param>
public sealed record DownloadFileQuery(
    Guid AccountId,
    string Path,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
