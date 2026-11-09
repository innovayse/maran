using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Mail.Commands.FlushMailQueue;

/// <summary>Asks the mail transfer agent to attempt delivery of every deferred message now; nothing is deleted.</summary>
/// <param name="IpAddress">The caller's address, stamped by the action, for the journal.</param>
/// <param name="UserAgent">The caller's client, stamped by the action, for the journal.</param>
public sealed record FlushMailQueueCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
