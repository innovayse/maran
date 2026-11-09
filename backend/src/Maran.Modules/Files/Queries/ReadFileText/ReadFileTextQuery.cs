using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Queries.ReadFileText;

/// <summary>Reads one file of an account's home as text, to be edited.</summary>
/// <remarks>
/// The only route that returns a file's content as part of a JSON document, and the reason it exists
/// is the editor: the SPA renders the string as text and never as markup. A file that is too large, holds
/// a NUL byte or is not valid UTF-8 is not editable and is offered for download only.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Path">The file, relative to the home. Never empty.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and stamped by
/// the action. Never bound from the request; a refused read is journalled under it.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers and
/// stamped by the action. Never bound from the request.</param>
public sealed record ReadFileTextQuery(
    Guid AccountId,
    string Path,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
