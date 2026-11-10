using System.Text;
using System.Text.Json;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Signs licence envelopes with a keypair that exists only in this test project, and hands the host the
/// matching public key to trust. The production verifier trusts the key a release embeds and never this
/// one: a test supplies its own through the container, exactly as the Licensing module's unit tests supply
/// theirs through a constructor.
/// </summary>
public static class SignedTestLicence
{
    /// <summary>The private half of the test keypair. Test-only; never a production secret and never a production key.</summary>
    private const string PrivateKeyHex = "DFD965CF86BE5E77A46584EF9D296E1260AC74F3002A2D256C57515C252B0DF6";

    /// <summary>The key a host under test trusts: the public half of the test keypair.</summary>
    public static LicenceVerificationKey TrustedKey
    {
        get
        {
            var privateKey = new Ed25519PrivateKeyParameters(Convert.FromHexString(PrivateKeyHex), 0);

            return LicenceVerificationKey.Of(privateKey.GeneratePublicKey().GetEncoded());
        }
    }

    /// <summary>Builds a signed licence envelope naming one module.</summary>
    /// <param name="moduleId">The module the licence unlocks.</param>
    /// <param name="expiry">When the licence expires.</param>
    /// <param name="offline">Whether the licence is an offline one, covered by its own expiry with no lease.</param>
    /// <returns>The raw envelope text, as the install endpoint takes it.</returns>
    public static string Naming(string moduleId, DateTimeOffset expiry, bool offline)
    {
        var payload = new Dictionary<string, object>
        {
            ["id"] = "lic-wire-test",
            ["product"] = "maran",
            ["tier"] = "included",
            ["modules"] = new[] { moduleId },
            ["expiry"] = expiry.ToString("O"),
            ["offline"] = offline,
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, new Ed25519PrivateKeyParameters(Convert.FromHexString(PrivateKeyHex), 0));
        signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        var signature = Convert.ToBase64String(signer.GenerateSignature());

        // The payload is embedded verbatim as an object, so the bytes the verifier reads are the bytes signed.
        return $$"""{"payload":{{payloadJson}},"signature":"{{signature}}"}""";
    }
}
