using System.Text;
using Maran.Host.Demo.Adapters;

namespace Maran.Host.Demo.Tests.Adapters;

/// <summary>The verifier trusts the demo key and nothing else, and never throws.</summary>
public sealed class DemoLicenceSignatureVerifierTests
{
    /// <summary>A payload to sign.</summary>
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("{\"tier\":\"demo\"}");

    /// <summary>What the key signed verifies.</summary>
    [Fact]
    public void A_signature_made_by_the_demo_key_verifies()
    {
        var key = new DemoLicenceKey();

        Assert.True(new DemoLicenceSignatureVerifier(key).Verify(Payload, key.Sign(Payload)));
    }

    /// <summary>A changed payload no longer verifies.</summary>
    [Fact]
    public void A_changed_payload_is_refused()
    {
        var key = new DemoLicenceKey();
        var signature = key.Sign(Payload);

        Assert.False(
            new DemoLicenceSignatureVerifier(key).Verify(Encoding.UTF8.GetBytes("{\"tier\":\"pro\"}"), signature));
    }

    /// <summary>A signature from another key (another process, or the real authority) is refused.</summary>
    [Fact]
    public void A_signature_from_another_key_is_refused()
    {
        var signature = new DemoLicenceKey().Sign(Payload);

        Assert.False(new DemoLicenceSignatureVerifier(new DemoLicenceKey()).Verify(Payload, signature));
    }

    /// <summary>A signature of the wrong length is refused without throwing.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(65)]
    public void A_signature_of_the_wrong_length_is_refused(int length)
    {
        var verifier = new DemoLicenceSignatureVerifier(new DemoLicenceKey());

        Assert.False(verifier.Verify(Payload, new byte[length]));
    }
}
