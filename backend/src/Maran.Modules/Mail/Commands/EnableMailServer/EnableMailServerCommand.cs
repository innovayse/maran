using System.Text.Json.Serialization;
using Maran.Modules.Mail.Interfaces;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.EnableMailServer;

/// <summary>
/// Turns the mail server on for this host with the given settings, or converges a server that is already on to them.
/// </summary>
/// <remarks>
/// <para>
/// The name is checked against the reserved set, stored lower case. It carries no certificate, no key and no path: the server presents the panel's own
/// certificate, which the agent takes from its fixed location, and the webmail master secret is not
/// part of this command at all.
/// </para>
/// <para>
/// The caller's address and client are stamped by the action and can never be sent by the caller
/// (rules/csharp.md "Server-established members of a command").
/// </para>
/// </remarks>
/// <param name="Hostname">The host name the server presents; it is also the name its certificate is for.</param>
/// <param name="Pop3Enabled">Whether POP3 and POP3S are served beside IMAP.</param>
/// <param name="AntivirusEnabled">Whether incoming mail is scanned for viruses.</param>
/// <param name="AllowExternalForwarding">Whether a forwarder may point outside its account's own mail domains.</param>
/// <param name="MaxMessageMib">The largest message accepted, in mebibytes.</param>
/// <param name="SpamGreylistScore">The spam score from which a message is greylisted.</param>
/// <param name="SpamAddHeaderScore">The spam score from which a spam header is added.</param>
/// <param name="SpamRejectScore">The spam score from which a message is rejected.</param>
/// <param name="IpAddress">The caller's address, stamped by the action, for the journal.</param>
/// <param name="UserAgent">The caller's client, stamped by the action, for the journal.</param>
public sealed record EnableMailServerCommand(
    string Hostname,
    bool Pop3Enabled,
    bool AntivirusEnabled,
    bool AllowExternalForwarding,
    int MaxMessageMib,
    int SpamGreylistScore,
    int SpamAddHeaderScore,
    int SpamRejectScore,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "") : IMailServerSettingsInput;
