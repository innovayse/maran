using Maran.Modules.Licensing.Domain.ValueObjects;
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
/// The public key is the one thing this design does NOT try to protect
/// (<c>docs/superpowers/notes/2026-09-22-licence-verification-threat-note.md</c> §4): it is meant to
/// be readable by anyone, and the security property it provides — a licence cannot be forged without
/// the matching PRIVATE key — does not depend on this source being hidden. It DOES depend on the
/// private half being secret, which is why this type holds no key of its own: the production key is
/// supplied by the release (<see cref="EmbeddedLicenceVerificationKeySource"/>) and a test supplies
/// its own through the constructor. With no key configured every signature is refused.
/// </para>
/// </remarks>
public sealed class Ed25519LicenceSignatureVerifier : ILicenceSignatureVerifier
{
    /// <summary>The key this verifier trusts, or none.</summary>
    private readonly LicenceVerificationKey _key;

    /// <summary>Creates the verifier over one trusted key.</summary>
    /// <param name="key">The only key a signature may verify under; <see cref="LicenceVerificationKey.None"/> refuses everything.</param>
    public Ed25519LicenceSignatureVerifier(LicenceVerificationKey key)
    {
        _key = key;
    }

    /// <inheritdoc />
    public bool HasVerificationKey
    {
        get { return _key.IsConfigured; }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Never throws: BouncyCastle's <c>Verify</c> itself only returns <c>false</c> for a
    /// wrong-length or malformed signature rather than throwing, and the constructor calls that
    /// could throw on a malformed PUBLIC key are given a key value whose length
    /// <see cref="LicenceVerificationKey"/> has already checked, never caller input — so there is nothing here for a caller-triggered
    /// exception to come from. Kept catch-free rather than wrapped, because a wrapping try/catch
    /// here would be exactly the kind of deletable safety net §229's structural argument
    /// says not to rely on; <see cref="Services.LicenceVerifier"/>
    /// is the one place that boundary is actually enforced, by never calling anything here with
    /// data that has not already been shaped by that method's own try/catch.
    /// </remarks>
    public bool Verify(byte[] payloadBytes, byte[] signature)
    {
        if (!_key.IsConfigured || signature.Length != Ed25519.SignatureSize)
        {
            return false;
        }

        var publicKeyParameters = new Ed25519PublicKeyParameters(_key.PublicKey, 0);
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, publicKeyParameters);
        verifier.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        return verifier.VerifySignature(signature);
    }
}
