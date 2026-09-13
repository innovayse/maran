using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Databases.Commands.CreateDatabase;

/// <summary>
/// Creates a MySQL database for an account, together with a dedicated user granted full privileges
/// on that database alone, and mints the password that user is created with (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// The command carries no password, and none may be added: the panel generates one
/// (<c>ProvisionedPasswordGenerator</c>) rather than accepting one, so there is no customer-chosen
/// value to validate, to transport, or to find in a request log.
/// </para>
/// <para>
/// It is bound directly by <c>DatabasesController.CreateAsync</c>; there is no separate request
/// type. <paramref name="IpAddress"/> and <paramref name="UserAgent"/> carry the guard that keeps a
/// caller from stating the values it is being audited by.
/// </para>
/// </remarks>
/// <param name="AccountId">The account that will own the database.</param>
/// <param name="Name">The database name the customer asked for, without the account prefix.</param>
/// <param name="DbUserName">
/// The dedicated user's name, without the account prefix. Chosen independently of
/// <paramref name="Name"/> because MySQL's two namespaces are independent and a customer may want
/// one user's name to say what it is for rather than to repeat the database's.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Read from the connection by the controller
/// and never bound from the request: <c>JsonIgnore</c> keeps it out of the JSON body — a
/// <c>[FromBody]</c> parameter is handed to an input formatter, which the model-binding pipeline
/// never sees, so <c>BindNever</c> alone would leave it writable — and <c>BindNever</c> keeps it out
/// of the query and form pipelines, where <c>JsonIgnore</c> means nothing. The two cover disjoint
/// pipelines, so both are always present. The default exists so that <c>[ApiController]</c>'s
/// implicit-required validation does not reject every body for omitting a field the body is
/// forbidden to carry.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Server-established and guarded exactly as
/// <paramref name="IpAddress"/> is, for the same reason.
/// </param>
public sealed record CreateDatabaseCommand(
    Guid AccountId,
    string Name,
    string DbUserName,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
