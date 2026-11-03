using Maran.Modules.Firewall.Domain.Enums;

namespace Maran.Modules.Firewall.Domain.ValueObjects;

/// <summary>
/// A named bundle of rules an operator opens so often that typing them into the rule form is
/// friction. A preset is a shortcut to the same allow requests the form sends, never a second way
/// of changing the firewall: every rule in it is validated by the allow command like a hand-typed one.
/// </summary>
/// <param name="Key">Which preset this is.</param>
/// <param name="Closable">
/// Whether the screen offers switching the preset off again by removing what it installed. A preset
/// whose rules a public service depends on is installed but not removed from the preset card; the
/// rules table removes them one by one, with the lockout confirmation, like any other rule.
/// </param>
/// <param name="Rules">The rules installing the preset sends, in the order they are sent.</param>
public sealed record FirewallPreset(FirewallPresetKey Key, bool Closable, IReadOnlyList<FirewallPresetRule> Rules);
