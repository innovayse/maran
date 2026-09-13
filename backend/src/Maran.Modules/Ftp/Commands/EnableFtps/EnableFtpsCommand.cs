using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Ftp.Commands.EnableFtps;

/// <summary>
/// Configures this server's FTPS daemon for one hostname and brings it up.
/// </summary>
/// <remarks>
/// <para>
/// <b>The command carries the hostname and one optional address, and NOTHING else.</b> The passive
/// port range and the concurrent-session ceiling are not the caller's to send: they are decided once
/// by <c>FtpsDefaults</c>, because the range has to be the same range an operator opened in the
/// firewall and the ceiling has to be a number that range can serve. Two operators editing two
/// numbers independently is how a daemon ends up advertising ports nothing lets through, and a
/// number typed into a form is a domain constant that has escaped into the SPA (rules/vue.md).
/// </para>
/// <para>
/// It carries no certificate, no key and no path to either. This module never writes certificate
/// material; the Ssl module owns that store, and the daemon here only reads whether material exists
/// for the hostname.
/// </para>
/// </remarks>
/// <param name="Hostname">
/// The hostname the daemon serves, which is also the name its certificate material is filed under
/// and the name the panel tells a customer to connect to. Refused unless this panel serves a site
/// for it.
/// </param>
/// <param name="PassiveAddress">
/// The address to advertise in the PASV reply for a host behind NAT whose public address is not the
/// one the socket is bound to. The ONE value an operator types, and optional — but the absence is
/// spelled EMPTY, not null, because that is the spelling the agent contract already defines: an
/// empty <c>passive_address</c> means the key is not written at all and the daemon answers with the
/// address the control connection arrived on, which is the ordinary host.
///
/// It was <c>string?</c> for one round, and that was a defect the request-contract gate caught: a
/// third spelling of "no address" between a screen whose blank field serializes as <c>""</c> and a
/// wire whose absent value IS <c>""</c>. An ordinary host — every host not behind NAT — would have
/// been refused at the form with a message about an invalid address.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record EnableFtpsCommand(
    string Hostname,
    string PassiveAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
