using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Databases.Commands.CreateDatabaseUser;

/// <summary>
/// Creates an additional database user for an account, granted nothing, and mints the password it is
/// created with.
/// </summary>
/// <remarks>
/// The command carries no password and none may be added: the panel generates one rather than accepting
/// one, so there is no customer-chosen value to validate, to transport, or to find in a request log.
/// The user can connect from the server itself only.
/// </remarks>
/// <param name="AccountId">The account that will own the user.</param>
/// <param name="NameSuffix">The login name the customer asked for, without the account prefix.</param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Server-established: read from the connection by
/// the controller and never bound from the request (<c>JsonIgnore</c> closes the JSON body,
/// <c>BindNever</c> the query and form; rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal and guarded like <paramref name="IpAddress"/>.</param>
public sealed record CreateDatabaseUserCommand(
    Guid AccountId,
    string NameSuffix,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
