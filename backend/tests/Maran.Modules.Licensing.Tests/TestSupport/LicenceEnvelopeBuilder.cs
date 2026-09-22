using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// Builds a signed licence envelope — the exact wire shape <c>LicenceVerifier</c> parses — for
/// tests. Stands in for the cabinet service that will eventually issue real licences (§232, out of
/// scope for this repository), which is why every fixture this project constructs is
/// self-signed against a test keypair rather than checked against a genuine Innovayse artefact; see
/// this module's log entry, "What I could not observe".
/// </summary>
internal static class LicenceEnvelopeBuilder
{
    /// <summary>A fixed instant fixtures are dated relative to, so a default expiry never depends on the ambient clock.</summary>
    private static readonly DateTimeOffset FixedReferenceInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a signed envelope with the given fields, signed by the given private key.</summary>
    /// <param name="privateKeyHex">The Ed25519 private key (hex) to sign the payload with.</param>
    /// <param name="id">The licence id.</param>
    /// <param name="product">The licensed product id.</param>
    /// <param name="tier">The plan tier.</param>
    /// <param name="modules">The licensed module ids.</param>
    /// <param name="expiry">The licence's expiry instant.</param>
    /// <param name="server">
    /// The machine-id this licence is bound to, or <see langword="null"/> to omit the claim
    /// entirely — which is what an unbound licence looks like on the wire, and is deliberately not
    /// the same as a claim present but empty.
    /// </param>
    /// <returns>The raw envelope text, ready to hand to <c>LicenceVerifier.VerifyAsync</c>.</returns>
    internal static string Build(
        string privateKeyHex,
        string id = "lic-001",
        string product = "maran",
        string tier = "included",
        string[]? modules = null,
        DateTimeOffset? expiry = null,
        string? server = null)
    {
        modules ??= ["databases", "sites"];
        var expiryValue = expiry ?? FixedReferenceInstant.AddDays(30);

        // A dictionary rather than an anonymous type, so "server" can be ABSENT rather than
        // present-and-null: the verifier treats those two differently, and a fixture that could
        // only produce the second could not express an unbound licence at all.
        var payload = new Dictionary<string, object>
        {
            ["id"] = id,
            ["product"] = product,
            ["tier"] = tier,
            ["modules"] = modules,
            ["expiry"] = expiryValue.ToString("O"),
        };

        if (server is not null)
        {
            payload["server"] = server;
        }

        var payloadJson = JsonSerializer.Serialize(payload);

        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var signature = Sign(privateKeyHex, payloadBytes);

        // The envelope's "payload" value must be embedded verbatim as a JSON object (not a string)
        // so LicenceVerifier's JsonElement.GetRawText() sees exactly the bytes that were signed.
        return $$"""
        {"payload":{{payloadJson}},"signature":"{{Convert.ToBase64String(signature)}}"}
        """;
    }

    /// <summary>
    /// Builds a signed envelope around an ARBITRARY payload JSON fragment, correctly signed — for
    /// tests that need a payload shape the field-carrying overload above cannot produce (a missing
    /// field, a wrong-typed field), while still isolating "the shape is not a licence" from "the
    /// signature does not verify".
    /// </summary>
    /// <param name="privateKeyHex">The Ed25519 private key (hex) to sign the payload with.</param>
    /// <param name="payloadJson">The exact JSON object text to sign and embed as <c>payload</c>.</param>
    /// <returns>The raw envelope text.</returns>
    internal static string BuildRaw(string privateKeyHex, string payloadJson)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var signature = Sign(privateKeyHex, payloadBytes);
        return $$"""
        {"payload":{{payloadJson}},"signature":"{{Convert.ToBase64String(signature)}}"}
        """;
    }

    /// <summary>Signs bytes with an Ed25519 private key.</summary>
    /// <param name="privateKeyHex">The private key, hex-encoded, 32 bytes.</param>
    /// <param name="payloadBytes">The bytes to sign.</param>
    /// <returns>The 64-byte detached signature.</returns>
    private static byte[] Sign(string privateKeyHex, byte[] payloadBytes)
    {
        var privateKey = new Ed25519PrivateKeyParameters(Convert.FromHexString(privateKeyHex), 0);
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, privateKey);
        signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        return signer.GenerateSignature();
    }
}
