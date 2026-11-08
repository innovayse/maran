using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Notifications.Commands.DismissAllNotifications;

/// <summary>Dismisses every notification the signed-in administrator has not yet dismissed.</summary>
/// <remarks>It dismisses only for the caller; other administrators keep every entry.</remarks>
/// <param name="IpAddress">The caller's address, for the audit journal; never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; never bound from the request.</param>
public sealed record DismissAllNotificationsCommand(
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
