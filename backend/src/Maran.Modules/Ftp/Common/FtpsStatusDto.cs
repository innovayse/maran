namespace Maran.Modules.Ftp.Common;

/// <summary>
/// What an administrator's FTPS screen is told: what the agent MEASURED on the host, plus the two
/// facts only the panel holds — the hostname it persisted and the control port it chose.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in here is what the panel asked for.</b> Every daemon fact is read back out of the
/// configuration the daemon was started against, on the host, by the agent. A status assembled from
/// the panel's own settings row would agree with the panel by construction: it would report a
/// healthy daemon on a server where the unit had crashed, and — the case
/// <see cref="ForcedTls"/> exists for — a green screen on a daemon taking passwords in the clear.
/// </para>
/// <para>
/// <b>This shape is admin-only, and <see cref="CertificatePath"/> is why it must stay so.</b> It is
/// an absolute path on the host, which is operator-facing text a customer must never be shown
/// (rules/security.md item 8). The one endpoint that returns this record carries
/// <c>AuthorizationPolicies.AdminOnly</c>, and no customer-facing screen may reuse the record.
/// </para>
/// </remarks>
/// <param name="Hostname">
/// The hostname the panel persisted when FTPS was last enabled, or <c>null</c> when it never has
/// been. A screen renders the absence AS absence: it must not compose a host name of its own, because
/// a customer told to connect to a name the certificate was not issued for gets a name-mismatch
/// warning and no way to tell it from an attack.
/// </param>
/// <param name="Enabled">
/// Whether the panel currently wants the daemon running. The panel's INTENTION, and the only field
/// here that is one — it is beside the measurements so a screen can show the disagreement rather
/// than pick a side.
/// </param>
/// <param name="Running">The service manager reports the FTPS unit active.</param>
/// <param name="ControlPortAnswered">
/// A TCP connect to the control port, made on the host itself, returned a <c>220</c> greeting. A
/// LOCAL probe, so "answering here" and "unreachable from the internet" are compatible: nothing in
/// this module reads a firewall, and a screen must not promise reachability from this field.
/// </param>
/// <param name="ForcedTls">
/// The live configuration still forces TLS on both the control and the data channel.
/// <c>false</c> whenever the agent could not say otherwise — no live configuration, a file it could
/// not resolve, an agent predating the field — because an unknown must never read as "encryption is
/// enforced". A screen that shows this as green while it is <c>false</c> is the exact defect the
/// field was put on the wire to prevent.
/// </param>
/// <param name="CertificatePresent">
/// Certificate material exists for <paramref name="Hostname"/>. When no hostname is persisted the
/// agent was asked about nothing, so this is <c>false</c> for "not asked" rather than for "absent" —
/// which is why a screen reads it together with <paramref name="Hostname"/> and not alone.
/// </param>
/// <param name="CertificateIsSelfSigned">
/// The material is the agent's own self-signed placeholder, read from the marker file beside it.
/// True means every customer connecting will see their client's certificate warning.
/// </param>
/// <param name="CertificatePath">Where the material lives, or would have to be placed. Operator-facing.</param>
/// <param name="ControlPort">
/// The control port this panel configures, from <c>FtpsDefaults</c>. It is here because the panel is
/// what tells an operator which port to open, and the number an operator is told has to be the
/// number the daemon was configured with.
/// </param>
/// <param name="PassivePortMin">The lowest passive data port the LIVE configuration carries; zero when there is none.</param>
/// <param name="PassivePortMax">The highest passive data port the LIVE configuration carries; zero when there is none.</param>
/// <param name="Ipv4Only">
/// The live configuration is the IPv4-only fallback the enable path writes when the kernel refuses
/// an IPv6 listening bind. Stated on the screen; nothing decides on it.
/// </param>
public sealed record FtpsStatusDto(
    string? Hostname,
    bool Enabled,
    bool Running,
    bool ControlPortAnswered,
    bool ForcedTls,
    bool CertificatePresent,
    bool CertificateIsSelfSigned,
    string CertificatePath,
    int ControlPort,
    uint PassivePortMin,
    uint PassivePortMax,
    bool Ipv4Only);
