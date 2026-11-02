namespace Maran.Agent.Client.Services.FtpsService;

/// <summary>
/// What the agent observed about this panel's own FTPS daemon: the facts every one of the
/// daemon rpcs answers with, field for field as <c>ftp.proto</c> declares them.
/// </summary>
/// <remarks>
/// <para>
/// One shape for four rpcs, because the contract gives four separate ok messages carrying the same
/// nine fields — <c>EnableFtpsOk</c>, <c>DisableFtpsOk</c>, <c>GetFtpsStatusOk</c> and
/// <c>ReloadFtpsTlsOk</c> — built in the agent from one observation. Four panel-side records would
/// be four places for the same facts to drift apart, and the screen that reads them cannot
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
/// <param name="Certificate">
/// What the agent found for the hostname it was asked about. With an empty hostname nothing was
/// asked, which this record reports as <see cref="FtpsCertificate.Absent"/>: the agent cannot tell
/// "no hostname" from "no material", so the caller that knows whether a hostname was sent says so.
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
/// <param name="TlsEnforcement">
/// Whether the live configuration still forces TLS on both the control and the data channel, in
/// three outcomes: <see cref="FtpsTlsEnforcement.Enforced"/>, <see cref="FtpsTlsEnforcement.NotEnforced"/>
/// (the configuration was read and TLS is optional) and <see cref="FtpsTlsEnforcement.NotObserved"/>
/// (the agent could not read the configuration at all). The last is not a spelling of the middle one:
/// a screen that says "TLS is not enforced" over a file nobody read has made a claim about the host
/// that nothing measured. A caller must never treat <see cref="FtpsTlsEnforcement.NotObserved"/> as
/// <see cref="FtpsTlsEnforcement.Enforced"/>.
/// </param>
public sealed record FtpsStatusDto(
    bool Running,
    bool ControlPortAnswered,
    FtpsCertificate Certificate,
    string CertificatePath,
    uint PassivePortMin,
    uint PassivePortMax,
    bool Ipv4Only,
    FtpsTlsEnforcement TlsEnforcement);
