using System.Text.Json.Serialization;
using Maran.Modules.Notifications.Domain.Enums;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Notifications.Commands.SaveSmtpSettings;

/// <summary>Replaces the panel's outgoing mail settings with the ones an administrator just entered.</summary>
/// <remarks>
/// <para>
/// One command for the whole row rather than one per field: the settings are a single working
/// configuration, and a panel that could be left with a new host and the old port would simply stop
/// sending mail with nothing on the screen to say why.
/// </para>
/// <para>
/// The command is bound directly by <c>SmtpSettingsController.SaveAsync</c>; there is no separate
/// request type. Everything from <paramref name="Host"/> to <paramref name="AlertRecipient"/> is the
/// administrator's to state; the last two fields are not, and carry the guard that says so.
/// </para>
/// </remarks>
/// <param name="Host">Host name or address of the mail server.</param>
/// <param name="Port">TCP port the mail server listens on.</param>
/// <param name="Security">How the connection is to be protected.</param>
/// <param name="Username">The submission user name, or empty when the server takes no credentials.</param>
/// <param name="Password">
/// The new password, or <c>null</c> to keep the stored one. The distinction is what makes the
/// settings form workable: the form cannot show the stored password, so it submits nothing when the
/// administrator did not retype one — and a save that read that as "clear it" would silently
/// unauthenticate the panel's mail the first time anybody changed the port. The empty string is
/// different and does clear it, which is what a move to a relay taking no credentials needs. The
/// nullability is therefore load-bearing and survives the removal of the request type unchanged:
/// omitting the field still deserializes to <c>null</c>, which still means "keep".
/// </param>
/// <param name="FromAddress">The address the panel's mail is sent from.</param>
/// <param name="FromName">The display name beside the sender address; may be empty.</param>
/// <param name="AlertRecipient">Where alert mail goes — the operator's own address.</param>
/// <param name="IpAddress">
/// The caller's address, for the audit journal. Read from the connection by the controller and never
/// bound from the request: <c>JsonIgnore</c> keeps it out of the JSON body — a <c>[FromBody]</c>
/// parameter is handed to an input formatter, which the model-binding pipeline never sees, so
/// <c>BindNever</c> alone would leave it writable — and <c>BindNever</c> keeps it out of the query
/// and form pipelines, where <c>JsonIgnore</c> means nothing. The two cover disjoint pipelines, so
/// both are always present. The default exists so that <c>[ApiController]</c>'s implicit-required
/// validation does not reject every body for omitting a field the body is forbidden to carry.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, for the audit journal. Server-established and guarded exactly as
/// <paramref name="IpAddress"/> is, for the same reason.
/// </param>
public sealed record SaveSmtpSettingsCommand(
    string Host,
    int Port,
    SmtpSecurity Security,
    string Username,
    string? Password,
    string FromAddress,
    string FromName,
    string AlertRecipient,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
