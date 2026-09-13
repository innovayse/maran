namespace Maran.Agent.Client.Services.FtpsService;

/// <summary>
/// What the agent observed about this panel's own FTPS daemon: the nine facts every one of the
/// daemon rpcs answers with, field for field as <c>ftp.proto</c> declares them.
/// </summary>
/// <remarks>
/// <para>
/// One shape for four rpcs, because the contract gives four separate ok messages carrying the same
/// nine fields — <c>EnableFtpsOk</c>, <c>DisableFtpsOk</c>, <c>GetFtpsStatusOk</c> and
/// <c>ReloadFtpsTlsOk</c> — built in the agent from one observation. Four panel-side records would
/// be four places for the same nine facts to drift apart, and the screen that reads them cannot
/// tell which call produced the answer it is showing.
/// </para>
/// <para>
/// Every field is what the agent MEASURED, never what the panel asked for. That is the property
/// the whole message exists for: a status assembled from the panel's own intention would agree
/// with the panel by construction and certify a daemon it had never looked at.
/// </para>
/// </remarks>
/// <param name="Running">The service manager reports the FTPS unit active.</param>
/// <param name="ControlPortAnswered">
/// A TCP connect to the control port, made on the host itself, returned a <c>220</c> greeting. A
/// LOCAL probe: it bypasses the firewall, so "answering here" and "unreachable from outside" are
/// compatible, and nothing in this client reads a firewall to tell them apart.
/// </param>
/// <param name="CertificatePresent">
/// Certificate material exists for the hostname that was asked about. With an empty hostname this
/// is <c>false</c> because nothing was asked, which is NOT the same as "no material exists".
/// </param>
/// <param name="CertificateIsSelfSigned">
/// The material is the agent's own self-signed placeholder — read from the marker file beside it,
/// never from a parse of the certificate.
/// </param>
/// <param name="CertificatePath">
/// Where the material lives, or would have to be placed. Operator-facing: it names an absolute path
/// on the host and must never be rendered to a customer (rules/security.md item 8).
/// </param>
/// <param name="PassivePortMin">The lowest passive data port the LIVE configuration carries; zero when there is none.</param>
/// <param name="PassivePortMax">The highest passive data port the LIVE configuration carries; zero when there is none.</param>
/// <param name="Ipv4Only">
/// The live configuration is the IPv4-only fallback the enable path writes when the kernel refuses
/// an IPv6 listening bind. <c>false</c> is the dual-stack default and also what a host with no
/// configuration yet reports.
/// </param>
/// <param name="ForcedTls">
/// The live configuration still forces TLS on both the control and the data channel. <c>false</c>
/// is the answer whenever the agent cannot say otherwise — no live configuration, a file it could
/// not resolve, or an agent predating the field — and that direction is deliberate: an unknown must
/// never read as "encryption is enforced".
/// </param>
public sealed record FtpsStatusDto(
    bool Running,
    bool ControlPortAnswered,
    bool CertificatePresent,
    bool CertificateIsSelfSigned,
    string CertificatePath,
    uint PassivePortMin,
    uint PassivePortMax,
    bool Ipv4Only,
    bool ForcedTls);
