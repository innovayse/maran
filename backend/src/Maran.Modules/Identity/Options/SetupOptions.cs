namespace Maran.Modules.Identity.Options;

/// <summary>
/// The one-time token the installer generates so the first administrator can be created in a
/// browser. Bound from the <c>Setup</c> configuration section; the value lives in
/// <c>/etc/maran/panel.env</c> and is printed to the operator's terminal, never to the install log
/// (rules/security.md item 8).
/// </summary>
/// <remarks>
/// Deliberately NOT <c>[Required]</c>. A panel whose setup is finished has no token, and demanding
/// one would refuse to boot every server that has been running for a week — the exact opposite of
/// what a required security setting should do.
///
/// It lives in this module rather than in the Host's <c>Configuration/</c> folder for the same
/// reason <c>JwtOptions</c> does: the code that reads it is here, and a module can never reference
/// the Host.
/// </remarks>
public sealed class SetupOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Setup";

    /// <summary>The one-time token, or an empty string once setup is done.</summary>
    public string Token { get; set; } = string.Empty;
}
