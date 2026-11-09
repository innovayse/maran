using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.DisableMailServer;

/// <summary>Stops this host's mail daemons so that nothing listens; data and accounts' mail stay.</summary>
/// <param name="IpAddress">The caller's address, stamped by the action, for the journal.</param>
/// <param name="UserAgent">The caller's client, stamped by the action, for the journal.</param>
public sealed record DisableMailServerCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
