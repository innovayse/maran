using System.Text.Json.Serialization;
using Maran.Modules.Databases.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Databases.Commands.ChangeDatabaseUserPrivileges;

/// <summary>
/// Makes the privileges one database user holds on one database exactly the set given. An empty set
/// removes the grant.
/// </summary>
/// <remarks>
/// The privilege list is the closed <see cref="DatabasePrivilege"/>, so GRANT OPTION, FILE, PROCESS,
/// SUPER and every global privilege cannot be named at all. The two identifiers come from the route and
/// are established by the server, so a body cannot aim one request at a user in the URL and a different
/// one in a field.
/// </remarks>
/// <param name="Privileges">The complete set to hold; each privilege at most once.</param>
/// <param name="UserId">The user whose privileges change. Stamped from the route and never bound.</param>
/// <param name="DatabaseId">The database the privileges are held on. Stamped from the route and never bound.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal. Server-established and guarded.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal. Server-established and guarded.</param>
public sealed record ChangeDatabaseUserPrivilegesCommand(
    IReadOnlyList<DatabasePrivilege> Privileges,
    [property: JsonIgnore][property: BindNever][BindNever] Guid UserId = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid DatabaseId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
