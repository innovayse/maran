namespace Maran.Modules.Notifications.Common;

/// <summary>The addresses operator alerts are mailed to, as the alerts screen reads them back.</summary>
/// <param name="Addresses">The addresses in list order; the first is the primary one. Empty when none is set.</param>
/// <param name="Maximum">The most addresses the list may hold, so the screen holds no cap of its own.</param>
public sealed record AlertRecipientsDto(IReadOnlyList<string> Addresses, int Maximum);
