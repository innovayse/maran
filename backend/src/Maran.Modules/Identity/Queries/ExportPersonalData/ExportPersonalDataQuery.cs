using System.Text.Json.Serialization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Identity.Queries.ExportPersonalData;

/// <summary>
/// Asks for everything the panel holds about one person or one hosting account (issue #146).
/// </summary>
/// <remarks>
/// <b>Every member is established by the server.</b> The two administrator-only routes of
/// <c>PrivacyController</c> each write the kind and the id from their own route, and the caller's
/// address and user agent from the connection; nothing here is bound from a body or a query string,
/// so the subject of an export is exactly the one the URL named (rules/csharp.md "Server-established
/// members of a command").
/// </remarks>
/// <param name="Kind">Whether the subject is a user or an account; written by the action from the route taken.</param>
/// <param name="SubjectId">The user's or the account's id, written by the action from the route's id.</param>
/// <param name="IpAddress">The caller's address, stamped by the action; it goes into the journal entry.</param>
/// <param name="UserAgent">The caller's user agent, stamped by the action; it goes into the journal entry.</param>
public sealed record ExportPersonalDataQuery(
    [property: JsonIgnore][property: BindNever][BindNever] PersonalDataSubjectKind Kind = default,
    [property: JsonIgnore][property: BindNever][BindNever] Guid SubjectId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
