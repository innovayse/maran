namespace Maran.Host.Configuration;

/// <summary>
/// The host name the panel itself is reached by. Bound from the <c>Panel</c> configuration section
/// (<c>Panel__HostName</c>), written by <c>installer/lib/60-config.sh</c> into
/// <c>/etc/maran/panel.env</c> from the same value the installer renders into the panel's own
/// virtual host.
/// </summary>
/// <remarks>
/// <para>
/// It exists for one reader, <see cref="Maran.Host.Modules.PanelHostDirectory"/>, which answers the
/// Sites, Ssl and Ftp modules' question "is this name the panel's own", so a tenant cannot put the
/// panel's name into a virtual host or a certificate request
/// (docs/superpowers/notes/2026-10-26-reserved-hostnames-threat-note.md). It is the operator's own
/// value, never read from a request.
/// </para>
/// <para>
/// <b>Absent is not an error, and absent reserves nothing.</b> The type is deliberately not
/// <c>[Required]</c>: a panel installed before this setting existed, and a development host, have
/// none, and demanding it would fail a boot with nothing wrong with it. The static reserved set
/// (<c>ReservedHostNameRule</c>) is unconditional, so a missing value narrows only the host-specific
/// half of the protection and never opens the static half.
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
}
