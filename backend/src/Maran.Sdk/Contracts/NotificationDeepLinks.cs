namespace Maran.Sdk.Contracts;

/// <summary>
/// The closed set of destinations a notification may point at, spelled as the names of the SPA's
/// routes. A producer names one of these in <c>PanelNotificationRaised.DeepLink</c>, and the
/// Notifications module stores only a name found here.
/// </summary>
/// <remarks>
/// <para>
/// <b>A name from a closed set, never a URL.</b> A notification is text a producer composed, some of
/// it from attacker-influenced input (a task subject, a service manager's words). If the deep link
/// were a free string, an entry could send an administrator who clicks it to an address of the
/// producer's choosing; as a name from this list it can only ever open a page the panel itself has.
/// The SPA maps a name to its own route and invents no destination of its own.
/// </para>
/// <para>
/// Constants and a list rather than an enum, for the reason every Sdk vocabulary is: a marketplace
/// module names destinations this assembly was never compiled knowing about, and the list is what a
/// module extends by being added here in the same change that adds its page.
/// </para>
/// </remarks>
public static class NotificationDeepLinks
{
    /// <summary>The background tasks page.</summary>
    public const string Tasks = "tasks";

    /// <summary>The backups page.</summary>
    public const string Backups = "backups";

    /// <summary>The backup destinations page.</summary>
    public const string BackupDestinations = "backup-destinations";

    /// <summary>The server monitoring page.</summary>
    public const string Monitoring = "monitoring";

    /// <summary>Every destination a notification may name.</summary>
    public static readonly IReadOnlyList<string> All = [Tasks, Backups, BackupDestinations, Monitoring];

    /// <summary>Whether a name is one of <see cref="All"/>.</summary>
    /// <param name="name">The candidate name; compared exactly, because a route name is.</param>
    /// <returns><c>true</c> when the name is a known destination.</returns>
    public static bool IsKnown(string? name)
    {
        return name is not null && All.Contains(name);
    }
}
