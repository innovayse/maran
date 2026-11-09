using System.Text.Json.Serialization;
using Maran.Modules.Files.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.CreateFileEntry;

/// <summary>Creates one empty file or one folder, with the folders on the way to it, in an account's home.</summary>
/// <remarks>
/// Refuses to replace anything: a name that is already taken, by anything, is a conflict and the
/// existing entry is left exactly as it was. Bound directly by <c>POST /api/v1/files/entries</c>
/// (rules/csharp.md "An endpoint binds its command directly").
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="Path">The new entry's path relative to the home; never the home itself.</param>
/// <param name="Kind">Whether to create an empty file or a folder.</param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record CreateFileEntryCommand(
    Guid AccountId,
    string Path,
    NewFileEntryKind Kind,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
