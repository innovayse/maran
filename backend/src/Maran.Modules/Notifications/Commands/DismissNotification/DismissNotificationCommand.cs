using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Notifications.Commands.DismissNotification;

/// <summary>Dismisses one notification for the signed-in administrator only.</summary>
/// <remarks>
/// The controller builds this from the route (<c>POST feed/{id}/dismiss</c>, no body). Who is
/// dismissing is never a member: it is read from the caller's own sign-in, so no request can dismiss
/// on another administrator's behalf.
/// </remarks>
/// <param name="NotificationId">
/// The notification, from the route. Server-established and guarded like the audit fields so a
/// second value in a body or a query string cannot compete with the route's.
/// </param>
/// <param name="IpAddress">The caller's address, for the audit journal; never bound from the request.</param>
/// <param name="UserAgent">The caller's user agent, for the audit journal; never bound from the request.</param>
public sealed record DismissNotificationCommand(
    [property: JsonIgnore][property: BindNever][BindNever] Guid NotificationId = default,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "");
