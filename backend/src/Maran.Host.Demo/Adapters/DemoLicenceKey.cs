using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Maran.Host.Demo.Adapters;

/// <summary>
/// An Ed25519 key pair generated when the demo process starts and never written anywhere.
/// <see cref="DemoLicenceSignatureVerifier"/> accepts only what it signed.
/// </summary>
/// <remarks>
/// The real verifier's embedded public key is not used: a licence signed by the real authority must not
/// unlock the demo's behaviour, and nothing outside this process can produce a signature this key accepts
/// (threat note T4). The demo seeds no licence: the key exists so that the verifier in the composed provider
/// is one no real licence satisfies, and a restart makes a new one. Installing a licence is refused to
/// visitors, and the installed text is kept in memory (<see cref="DemoLicenceStore"/>), never in a file.
/// </remarks>
public sealed class DemoLicenceKey
{
    /// <summary>Signs.</summary>
    private readonly Ed25519PrivateKeyParameters _privateKey;

    /// <summary>Verifies.</summary>
    private readonly Ed25519PublicKeyParameters _publicKey;

    /// <summary>Generates a fresh key pair.</summary>
    public DemoLicenceKey()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        _privateKey = (Ed25519PrivateKeyParameters)pair.Private;
        _publicKey = (Ed25519PublicKeyParameters)pair.Public;
    }

    /// <summary>Signs a licence payload.</summary>
    /// <param name="payloadBytes">The exact bytes to sign.</param>
    /// <returns>The 64-byte detached signature.</returns>
    public byte[] Sign(byte[] payloadBytes)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);
        signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        return signer.GenerateSignature();
    }

    /// <summary>Checks a signature made by <see cref="Sign"/>.</summary>
    /// <param name="payloadBytes">The bytes that were signed.</param>
    /// <param name="signature">The detached signature.</param>
    /// <returns><c>true</c> only for a valid signature of these bytes by this key; never throws.</returns>
    public bool Verify(byte[] payloadBytes, byte[] signature)
    {
        if (signature.Length != Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.SignatureSize)
        {
            return false;
        }

        var verifier = new Ed25519Signer();
        verifier.Init(false, _publicKey);
        verifier.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        return verifier.VerifySignature(signature);
    }
}
