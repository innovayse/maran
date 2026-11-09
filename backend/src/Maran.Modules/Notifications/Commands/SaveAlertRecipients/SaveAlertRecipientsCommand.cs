using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Notifications.Commands.SaveAlertRecipients;

/// <summary>Replaces the whole list of addresses operator alerts are mailed to.</summary>
/// <remarks>
/// A whole-list replace rather than add and remove: the list is at most ten short addresses, the
/// screen edits it as one form, and one operation is one journal entry. An empty list is allowed: the
/// operator may want alerts nowhere by mail. The command is bound directly by
/// <c>AlertRecipientsController.SaveAsync</c>; there is no separate request type.
/// </remarks>
/// <param name="Addresses">
/// The addresses in the order the operator wants them; the first is the primary one. Compared
/// case-insensitively and stored lower-cased.
/// </param>
/// <param name="IpAddress">
/// The caller's address, for the audit journal. Server-established: <c>JsonIgnore</c> keeps it out of
/// the JSON body and <c>BindNever</c> out of the query and form pipelines, which are disjoint
/// (rules/csharp.md "Server-established members of a command").
/// </param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; guarded exactly as <paramref name="IpAddress"/>.</param>
public sealed record SaveAlertRecipientsCommand(
    IReadOnlyList<string> Addresses,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
