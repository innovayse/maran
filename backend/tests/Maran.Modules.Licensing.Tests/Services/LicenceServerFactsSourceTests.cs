using Maran.Modules.Licensing.Tests.TestSupport;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="Maran.Modules.Licensing.Services.LicenceServerFactsSource"/>: the id is the very value the
/// licence comparison reads, returned as read, and the link is built from it and the configured address.
/// </summary>
public sealed class LicenceServerFactsSourceTests
{
    /// <summary>A machine id as the marketplace spells one.</summary>
    private const string ServerId = "0123456789abcdef0123456789abcdef";

    /// <summary>The id is returned exactly as the identity source reports it and the link carries it.</summary>
    [Fact]
    public async Task The_server_id_is_the_one_the_identity_source_reports_and_the_link_carries_it()
    {
        var facts = await LicensingTestContext.ServerFacts(ServerId).ReadAsync(CancellationToken.None);

        Assert.Equal(ServerId, facts.ServerId);
        Assert.Equal("https://plugins-maran.innovayse.com/claim?server=" + ServerId, facts.ClaimUrl);
    }

    /// <summary>A host that reports an id of another shape shows it as read and gets no link, which the control above does get.</summary>
    [Fact]
    public async Task An_id_the_marketplace_would_refuse_is_shown_as_read_without_a_link()
    {
        var facts = await LicensingTestContext.ServerFacts("not-a-machine-id").ReadAsync(CancellationToken.None);

        Assert.Equal("not-a-machine-id", facts.ServerId);
        Assert.Null(facts.ClaimUrl);
    }

    /// <summary>A host whose id cannot be read has neither an id nor a link, and nothing is invented.</summary>
    [Fact]
    public async Task A_host_whose_id_cannot_be_read_has_no_id_and_no_link()
    {
        var facts = await LicensingTestContext.ServerFacts(null).ReadAsync(CancellationToken.None);

        Assert.Null(facts.ServerId);
        Assert.Null(facts.ClaimUrl);
    }

    /// <summary>A configured address that is not an https origin leaves the id and drops only the link.</summary>
    [Fact]
    public async Task A_configured_address_that_is_not_usable_drops_the_link_and_keeps_the_id()
    {
        var facts = await LicensingTestContext.ServerFacts(ServerId, "http://plugins-maran.innovayse.com")
            .ReadAsync(CancellationToken.None);

        Assert.Equal(ServerId, facts.ServerId);
        Assert.Null(facts.ClaimUrl);
    }

    /// <summary>A configured address is used instead of the documented one.</summary>
    [Fact]
    public async Task A_configured_address_replaces_the_documented_one()
    {
        var facts = await LicensingTestContext.ServerFacts(ServerId, "https://claims.example.test:8443")
            .ReadAsync(CancellationToken.None);

        Assert.Equal("https://claims.example.test:8443/claim?server=" + ServerId, facts.ClaimUrl);
    }
}
