namespace Maran.Modules.Marketplace.Models;

/// <summary>The words a catalogue module is shown with, already in the caller's language.</summary>
/// <param name="TierDisplayName">The licence tier's name.</param>
/// <param name="EntitlementName">The entitlement state's name.</param>
/// <param name="Reason">Why the newest version cannot be installed now, or null when it can.</param>
public sealed record ModuleTexts(string TierDisplayName, string EntitlementName, string? Reason);
