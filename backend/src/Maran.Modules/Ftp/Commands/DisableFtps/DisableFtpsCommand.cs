using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Ftp.Commands.DisableFtps;

/// <summary>
/// Stops this server's FTPS daemon and takes it out of the boot sequence.
/// </summary>
/// <remarks>
/// It carries nothing an operator types, because there is nothing to say: one server has one daemon
/// and stopping it takes no argument. In particular it names no login and removes none — disabling a
/// service is not revoking credentials, and an operator who switches FTPS back on expects the same
/// customers to be able to sign in.
/// </remarks>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Established by the server and stamped by the
/// action; never bound from the request, which is the thing being audited.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Established by the server and stamped by
/// the action; never bound from the request.
/// </param>
public sealed record DisableFtpsCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
