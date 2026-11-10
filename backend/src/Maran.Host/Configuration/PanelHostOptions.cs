namespace Maran.Host.Configuration;

/// <summary>
/// The host names the panel itself is reached by. Bound from the <c>Panel</c> configuration section:
/// <c>Panel__HostName</c>, written by <c>installer/lib/60-config.sh</c> into
/// <c>/etc/maran/panel.env</c> from the same value the installer renders into the panel's own
/// virtual host, and <c>Panel__PublicUrl</c>, the address the operator tells customers the panel lives at.
/// </summary>
/// <remarks>
/// <para>
/// It exists for one reader, <see cref="Maran.Host.Modules.PanelHostDirectory"/>, which answers the
/// Sites, Ssl, Dns and Ftp modules' question "is this name the panel's own", so a tenant cannot put the
/// panel's name into a virtual host, a zone or a certificate request
/// (docs/superpowers/notes/2026-10-26-reserved-hostnames-threat-note.md). Both are the operator's own
/// values, never read from a request, and the two may differ: <c>hostname -f</c> is not always the
/// name the operator advertises (issue #468), so reserving only <see cref="HostName"/> left the
/// advertised name open to a tenant.
/// </para>
/// <para>
/// <b>Absent is not an error, and absent reserves nothing.</b> The type is deliberately not
/// <c>[Required]</c>: a panel installed before this setting existed, and a development host, have
/// none, and demanding it would fail a boot with nothing wrong with it. The static reserved set
/// (<c>ReservedHostNameRule</c>) is unconditional, so a missing value narrows only the host-specific
/// half of the protection and never opens the static half.
/// </para>
/// <para>
/// <see cref="PublicUrl"/> is the same <c>Panel__PublicUrl</c> the Identity module's own options read to
/// build every invitation and password-reset link, and Identity validates it at start-up and refuses to
/// boot on a malformed one. Here it is read a second time for one purpose, the host it names; a value
/// that is not an acceptable origin, or whose host is an address, contributes no name (the failing
/// direction is permissive, and the address-shaped names are in the static set anyway).
/// </para>
/// </remarks>
public sealed class PanelHostOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Panel";

    /// <summary>
    /// The panel's host name, or <see langword="null"/> or empty when none is configured. A value
    /// that is not a well-formed host name, or is the installer's placeholder (<c>_</c>) or
    /// <c>localhost</c>, is treated as not configured.
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>
    /// The panel's public address (<c>Panel__PublicUrl</c>), an https origin, or <see langword="null"/>
    /// or empty when none is configured. Only its host is used, and only when it is a DNS name.
    /// </summary>
    public string? PublicUrl { get; set; }
}
