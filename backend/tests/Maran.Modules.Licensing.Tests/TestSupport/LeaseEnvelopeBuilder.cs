using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// Builds a signed lease file for the tests, the way the issuing service would: a payload and an
/// Ed25519 signature over the payload's exact text. Only ever signs with a TEST seed.
/// </summary>
internal static class LeaseEnvelopeBuilder
{
    /// <summary>Builds the text of a lease file.</summary>
    /// <param name="privateKeyHex">The seed to sign with.</param>
    /// <param name="licence">The licence id the lease renews.</param>
    /// <param name="issued">When the lease was issued.</param>
    /// <param name="validUntil">When the lease stops being valid.</param>
    /// <param name="server">The machine id the lease is bound to, or null to leave the member out.</param>
    /// <param name="kind">The payload's kind member; <c>lease</c> unless a test plants another.</param>
    /// <returns>The file text.</returns>
    internal static string Build(
        string privateKeyHex,
        string licence,
        DateTimeOffset issued,
        DateTimeOffset validUntil,
        string? server = null,
        string kind = "lease")
    {
        var payload = new Dictionary<string, object>
        {
            ["kind"] = kind,
            ["licence"] = licence,
            ["issued"] = issued.ToString("O"),
            ["validUntil"] = validUntil.ToString("O"),
        };

        if (server is not null)
        {
            payload["server"] = server;
        }

        return BuildRaw(privateKeyHex, JsonSerializer.Serialize(payload));
    }

    /// <summary>Signs an arbitrary payload text, for the malformed-payload cases.</summary>
    /// <param name="privateKeyHex">The seed to sign with.</param>
    /// <param name="payloadJson">The payload's exact JSON text.</param>
    /// <returns>The file text.</returns>
    internal static string BuildRaw(string privateKeyHex, string payloadJson)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var privateKey = new Ed25519PrivateKeyParameters(Convert.FromHexString(privateKeyHex), 0);
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, privateKey);
        signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        var signature = signer.GenerateSignature();

        return $$"""
        {"payload":{{payloadJson}},"signature":"{{Convert.ToBase64String(signature)}}"}
        """;
    }
}
