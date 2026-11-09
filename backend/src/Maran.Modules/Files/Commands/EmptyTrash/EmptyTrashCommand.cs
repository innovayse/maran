using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Files.Commands.EmptyTrash;

/// <summary>Removes everything in the account's trash for good.</summary>
/// <remarks>
/// Has no body: <c>DELETE /api/v1/files/trash</c> builds it from the query, so the guard attributes below
/// are a property of the type, kept for the day it becomes body-bound.
/// </remarks>
/// <param name="AccountId">
/// The account whose home it is, resolved in the handler. The resolution is the tenant boundary:
/// another tenant's account answers "not found".
/// </param>
/// <param name="IpAddress">The caller's address, established by the server from the connection and
/// stamped by the action. Never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, established by the server from the request headers
/// and stamped by the action. Never bound from the request.</param>
public sealed record EmptyTrashCommand(
    Guid AccountId,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
