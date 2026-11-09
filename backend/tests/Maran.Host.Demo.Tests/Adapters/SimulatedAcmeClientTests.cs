using System.Security.Cryptography.X509Certificates;
using Maran.Host.Demo.Adapters;
using Maran.Host.Demo.Tests.Fixtures;
using Maran.Modules.Ssl.Models;

namespace Maran.Host.Demo.Tests.Adapters;

/// <summary>The simulated authority issues a real, short-lived, self-signed certificate and contacts nobody.</summary>
public sealed class SimulatedAcmeClientTests
{
    /// <summary>The order carries the domain; the challenge target is never used.</summary>
    private static readonly AcmeOrderRequest Order = new("site.example.test", null!);

    /// <summary>The certificate parses, matches its key, names the domain and expires on schedule.</summary>
    [Fact]
    public async Task The_certificate_is_valid_for_the_domain_and_expires_after_the_lifetime()
    {
        var clock = new FixedClock();
        var client = new SimulatedAcmeClient(clock);

        var result = await client.OrderAsync(Order, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var issued = result.Match(
            value => { return value; },
            error => { throw new InvalidOperationException(error.Code); });
        Assert.Equal(clock.UtcNow + SimulatedAcmeClient.Lifetime, issued.NotAfter);
        using var certificate = X509Certificate2.CreateFromPem(issued.CertificatePem, issued.PrivateKeyPem);
        Assert.True(certificate.HasPrivateKey);
        Assert.Contains(
            "site.example.test",
            certificate.GetNameInfo(X509NameType.DnsName, false),
            StringComparison.Ordinal);
    }

    /// <summary>Two orders never share a key.</summary>
    [Fact]
    public async Task Every_order_gets_its_own_key()
    {
        var client = new SimulatedAcmeClient(new FixedClock());

        var firstOrder = await client.OrderAsync(Order, CancellationToken.None);
        var secondOrder = await client.OrderAsync(Order, CancellationToken.None);
        var first = firstOrder.Match(v => { return v; }, e => { throw new InvalidOperationException(e.Code); });
        var second = secondOrder.Match(v => { return v; }, e => { throw new InvalidOperationException(e.Code); });

        Assert.NotEqual(first.PrivateKeyPem, second.PrivateKeyPem);
    }
}
