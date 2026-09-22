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
    /// UNREACHABLE in this slice. §228 wants a fingerprint built from <c>machine-id</c> plus the
    /// primary network interface, and nothing in this tree exposes either today —
    /// <c>proto/agent/v1/system.proto</c>'s <c>AgentInfo</c> carries only <c>version</c>,
    /// <c>distro_id</c>, <c>family</c>, <c>proto_version</c> and <c>backup_root</c>. Obtaining a
    /// fingerprint needs a new agent RPC surface this slice was explicitly told not to invent, so
    /// <see cref="Services.LicenceVerifier"/> never produces this member — it is declared here so
    /// the enum and the wire contract are ready for the RPC surface once it exists, not because any
    /// code path returns it yet.
    /// </remarks>
    FingerprintMismatch,
}
