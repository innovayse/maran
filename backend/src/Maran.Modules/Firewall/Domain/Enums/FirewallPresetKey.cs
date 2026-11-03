namespace Maran.Modules.Firewall.Domain.Enums;

/// <summary>
/// Names a port preset the rule screen offers. The member name is both the preset's identity on the
/// wire (serialized camelCase) and the suffix of the resource key that names it for the operator
/// (<c>FirewallPreset</c> plus the member name).
/// </summary>
public enum FirewallPresetKey
{
    /// <summary>The ports a public web server answers on.</summary>
    Web,

    /// <summary>MySQL's port, opened to the outside world.</summary>
    MysqlExternal,
}
