using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Interfaces;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Ed25519 verification of a lease against the lease key pinned in the build, with the same primitive
/// and library as <see cref="Ed25519LicenceSignatureVerifier"/> (rules/security.md item 9: no
/// home-grown crypto). Stateless; the key arrives through the constructor so a test supplies its own
/// and no test key is ever the production key.
/// </summary>
public sealed class Ed25519LeaseSignatureVerifier : ILeaseSignatureVerifier
{
    /// <summary>The pinned lease key, or none.</summary>
    private readonly LeaseVerificationKey _key;

    /// <summary>Creates the verifier.</summary>
    /// <param name="key">The lease key this build trusts.</param>
    public Ed25519LeaseSignatureVerifier(LeaseVerificationKey key)
    {
        _key = key;
    }

    /// <inheritdoc />
    public bool HasVerificationKey
    {
        get { return _key.IsConfigured; }
    }

    /// <inheritdoc />
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
