namespace Maran.Modules.Notifications.Common;

/// <summary>How many entries the signed-in administrator has not dismissed, capped for a badge.</summary>
/// <param name="Count">The number, never above the cap.</param>
/// <param name="Capped"><c>true</c> when there are more than <paramref name="Count"/>.</param>
public sealed record NotificationCountDto(int Count, bool Capped);
