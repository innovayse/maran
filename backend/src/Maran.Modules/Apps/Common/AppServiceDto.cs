using Maran.Modules.Apps.Domain.Enums;

namespace Maran.Modules.Apps.Common;

/// <summary>What an app's service is doing after an administrator started, stopped, restarted or reloaded it.</summary>
/// <param name="ServiceStatus">What the service manager reports after the action.</param>
/// <param name="ServiceStatusName">The status in the request's language.</param>
public sealed record AppServiceDto(AppServiceStatus ServiceStatus, string ServiceStatusName);
