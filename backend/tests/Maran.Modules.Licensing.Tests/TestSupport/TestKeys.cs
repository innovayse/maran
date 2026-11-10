using Maran.Modules.Licensing.Domain.ValueObjects;
using Org.BouncyCastle.Crypto.Parameters;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// The keypairs this test project signs fixtures with. They exist ONLY here: the production verifier
/// trusts a key the release embeds and never one of these, and the architecture test
/// `LicenceVerificationKeyTests` fails the build if the production key ever equals the public half of
/// any private key committed under backend/tests. <see cref="TrustedKey"/> is handed to the verifier
/// through its constructor, which is the only way a test key reaches one.
/// </summary>
internal static class TestKeys
{
    /// <summary>
    /// The private half of the keypair a test verifier trusts. Test-only; never a production secret,
    /// and never the production key (generated fresh for this test project, unrelated to any key a
    /// release has used).
    /// </summary>
    internal const string RealPrivateKeyHex = "12B46979DD11C6026968FD7F9DA2B4237604C7049C5C03FDC9CE7CFBA5AB5E48";

    /// <summary>An unrelated keypair's private half, used to sign a licence the trusted key must reject.</summary>
    internal const string OtherPrivateKeyHex = "C46777FD2B7B516C68A6649CC1F7E1538E840F5462AFB5A3737E676DF2545F50";

    /// <summary>
    /// A TEST lease-signing seed. Committed on purpose: it signs only fixtures, and a build trusts its public
    /// half only because a test hands it to the verifier. It is NOT the licence seed above (the two keys are
    /// separate by design), and it is not a production key.
    /// </summary>
    internal const string LeasePrivateKeyHex = "7F3C1A5E9D2B4860A1C3E5F7091B2D4F6A8C0E2B4D6F8091A3C5E7F90B1D3F57";

    /// <summary>The lease key a test verifier trusts: the public half of <see cref="LeasePrivateKeyHex"/>.</summary>
    internal static LeaseVerificationKey TrustedLeaseKey
    {
        get
        {
            var privateKey = new Ed25519PrivateKeyParameters(Convert.FromHexString(LeasePrivateKeyHex), 0);
            return LeaseVerificationKey.Of(privateKey.GeneratePublicKey().GetEncoded());
        }
    }

    /// <summary>The public half of <see cref="RealPrivateKeyHex"/>, as the key a test verifier trusts.</summary>
    internal static LicenceVerificationKey TrustedKey
    {
        get
        {
            var privateKey = new Ed25519PrivateKeyParameters(Convert.FromHexString(RealPrivateKeyHex), 0);
            return LicenceVerificationKey.Of(privateKey.GeneratePublicKey().GetEncoded());
        }
    }
}
