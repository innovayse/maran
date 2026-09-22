using Maran.Modules.Licensing.Interfaces;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// <see cref="ILicenceSignatureVerifier"/> implemented over Ed25519, verification-only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the BCL.</b> <c>System.Security.Cryptography</c> has no Ed25519 support on net9.0 —
/// verified directly by reflecting over <c>RSA</c>'s own assembly for an <c>Ed25519</c>/<c>Ed448</c>
/// member and finding none, rather than assumed from the design note that expected the BCL to carry
/// it. BouncyCastle.Cryptography is used instead: a widely-audited, standard verification library,
/// not a home-grown implementation (rules/security.md item 9 forbids the latter, not a third-party
/// library used only for its own well-reviewed primitive), and it is already a transitive
/// dependency of this solution at the exact version pinned here.
/// </para>
/// <para>
/// The public key embedded below is the one thing this design does NOT try to protect
/// (<c>docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md</c> §4): it is meant to
/// be readable by anyone, and the security property it provides — a licence cannot be forged without
/// the matching PRIVATE key — does not depend on this source being hidden.
/// </para>
/// <para>
/// <b>No production key exists yet.</b> There is no real Innovayse-issued licence and no real
/// keypair to embed (see this module's log entry, "What I could not observe"). The 32-byte value
/// below is a placeholder generated for this pass so the type compiles and its own tests can sign
/// and verify against it; it MUST be replaced with Innovayse's real public key before this module
/// is ever wired into a release build, which is why it is a single named constant here rather than
/// scattered.
/// </para>
/// </remarks>
public sealed class Ed25519LicenceSignatureVerifier : ILicenceSignatureVerifier
{
    /// <summary>
    /// PLACEHOLDER — the embedded Ed25519 public key, 32 bytes. Generated for this pass only; see
    /// the type's own remarks. Not a secret (rules/security.md item 8's "what may safely be public"
    /// side): a public key is meant to be read by anyone who has this repository's source.
    /// </summary>
    private static readonly byte[] EmbeddedPublicKey =
        Convert.FromHexString("64DBCB0E66E14A3D40D255770FD6BB81000FC3BDA5F30A199011457E1B717B68");

    /// <inheritdoc />
    /// <remarks>
    /// Never throws: BouncyCastle's <c>Verify</c> itself only returns <c>false</c> for a
    /// wrong-length or malformed signature rather than throwing, and the constructor calls that
    /// could throw on a malformed PUBLIC key are given a value this type controls (a constant of
    /// the correct length), never caller input — so there is nothing here for a caller-triggered
    /// exception to come from. Kept catch-free rather than wrapped, because a wrapping try/catch
    /// here would be exactly the kind of deletable safety net §229's structural argument
    /// says not to rely on; <see cref="Services.LicenceVerifier"/>
    /// is the one place that boundary is actually enforced, by never calling anything here with
    /// data that has not already been shaped by that method's own try/catch.
    /// </remarks>
    public bool Verify(byte[] payloadBytes, byte[] signature)
    {
        if (signature.Length != Ed25519.SignatureSize)
        {
            return false;
        }

        var publicKeyParameters = new Ed25519PublicKeyParameters(EmbeddedPublicKey, 0);
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, publicKeyParameters);
        verifier.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        return verifier.VerifySignature(signature);
    }
}
