namespace Maran.Modules.Licensing.Domain.Enums;

/// <summary>
/// The closed set of reasons an installed licence artefact was not accepted (spec §228). Each
/// member has its own operator-facing sentence in <c>Resources/ErrorMessages.resx</c> so an
/// operator reads a specific, actionable fact — never a bare "invalid license" that tells them
/// nothing they can act on.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a closed enum, not <c>Maran.Sdk.Contracts.AuditActions</c>-style open constants: a
/// marketplace module can add an audit action this assembly never knew about, but there is no
/// seventh way for an Ed25519-signed, fingerprinted, expiring JSON document to be unacceptable that
/// a third party would ever need to add. A closed set lets <c>LicenceVerifier</c>'s mapping be
/// exhaustive.
/// </para>
/// <para>
/// <c>Absent</c> is NOT a member of this enum on purpose — see
/// <see cref="LicenceStatus"/>. "Nothing installed" and "something was installed and it failed"
/// are different facts an operator needs told apart, and folding the first into
/// <c>Refused(reason)</c> is the exact trap this design was asked to avoid.
/// </para>
/// </remarks>
public enum LicenceRefusalReason
{
    /// <summary>
    /// The installed text could not be parsed as a licence envelope at all — not JSON, or missing
    /// a field a licence must carry. Distinct from <see cref="SignatureInvalid"/>: a signature
    /// check is never attempted against bytes that are not even shaped like a licence.
    /// </summary>
    Malformed,

    /// <summary>
    /// The envelope parsed, but its Ed25519 signature does not verify against the embedded public
    /// key for the payload bytes it was verified against. The file was edited, truncated, or is a
    /// forgery.
    /// </summary>
    SignatureInvalid,

    /// <summary>The licence verified, but its stated expiry is on or before the clock's current instant.</summary>
    Expired,

    /// <summary>The licence verified, but it was issued for a different product.</summary>
    ProductMismatch,

    /// <summary>
    /// The licence verified, but its embedded server fingerprint does not match this server's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REACHABLE since the binding was wired: <c>GetServerFingerprintInputs</c> gives the panel
    /// this host's machine-id, and <c>LicenceServerBindingPolicy</c> compares it against the
    /// licence's optional <c>server</c> claim. A licence naming no server never produces this
    /// member; one naming a different server always does.
    /// </para>
    /// <para>
    /// <b>It also covers the case where this host's identity could not be read at all</b> — the
    /// agent is down, or the host has no machine-id. That is deliberate and it is the direction
    /// that costs an honest customer something: their bound licence reads as refused while their
    /// agent is down. The other direction would let anyone who can stop the agent run a bound
    /// licence on any machine they like, which is the attack binding exists to stop. The policy's
    /// own remarks carry the full argument and the condition under which to revisit it.
    /// </para>
    /// <para>
    /// The fingerprint is the machine-id ALONE. §228 also names the primary network interface, and
    /// the panel reads it but never compares it: measured on the development host it is WiFi, and
    /// it moves with a DHCP renewal, a VPN, or a cable in a different port.
    /// </para>
    /// </remarks>
    FingerprintMismatch,
}
