using Maran.Modules.Firewall.Domain.Enums;

namespace Maran.Modules.Firewall.Common;

/// <summary>One port preset the rule screen can offer, with the exact rules it installs.</summary>
/// <param name="Key">
/// The preset's identity, serialized as the camelCase member name (<c>web</c>, <c>mysqlExternal</c>).
/// </param>
/// <param name="Name">
/// The preset as an operator reads it, resolved for the request's culture; the member name itself
/// for a preset this build carries no entry for.
/// </param>
/// <param name="Closable">
/// Whether the screen offers switching the preset off again: when true it is drawn as a switch whose
/// off position removes every running rule on the preset's ports, when false as a button that only installs.
/// </param>
/// <param name="Rules">The rules installing the preset sends, in the order they are sent.</param>
public sealed record FirewallPresetDto(
    FirewallPresetKey Key,
    string Name,
    bool Closable,
    IReadOnlyList<FirewallRuleDto> Rules);
