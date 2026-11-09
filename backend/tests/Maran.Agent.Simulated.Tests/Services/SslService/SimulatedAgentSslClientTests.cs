using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Services.SslService;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.SslService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Simulated.Tests.Services.SslService;

/// <summary>Placing TLS material on the host: what is parsed, what is refused, and what is never kept.</summary>
public sealed class SimulatedAgentSslClientTests
{
    /// <summary>The domain of the site the tests install into.</summary>
    private const string Domain = "shop.example.test";

    /// <summary>What the site is, as the panel passes it; the simulated agent does not read it.</summary>
    private static readonly SiteDescriptor Descriptor = new(
        [],
        SiteBackendKind.Static,
        string.Empty,
        string.Empty,
        true,
        SiteOptionsDescriptor.None);

    /// <summary>The source of the current moment.</summary>
    private readonly FixedClock _clock = new();

    /// <summary>The expiry returned is the one in the certificate, and the ledger records it.</summary>
    [Fact]
    public async Task Installing_a_certificate_returns_the_expiry_read_from_it()
    {
        var (ledger, client) = NewHost();
        var material = TestCertificates.Rsa(_clock.UtcNow.AddDays(90));

        var installed = await InstallAsync(client, "acme", Domain, material);

        Assert.True(installed.IsSuccess);
        Assert.Equal(material.NotAfter, installed.Value.ExpiresAt);
        Assert.True(ledger.Certificates.TryGet(Domain, out var record));
        Assert.NotNull(record);
        Assert.Equal(material.NotAfter, record.ExpiresAt);
        Assert.Equal(_clock.UtcNow, record.InstalledAt);
    }

    /// <summary>A certificate issued for an elliptic-curve key is read like an RSA one.</summary>
    [Fact]
    public async Task An_elliptic_curve_certificate_is_read_like_an_rsa_one()
    {
        var (_, client) = NewHost();
        var material = TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(60));

        var installed = await InstallAsync(client, "acme", Domain, material);

        Assert.Equal(material.NotAfter, installed.Value.ExpiresAt);
    }

    /// <summary>Installing again replaces the record, which is how a renewal arrives.</summary>
    [Fact]
    public async Task Installing_again_replaces_the_certificate()
    {
        var (ledger, client) = NewHost();
        await InstallAsync(client, "acme", Domain, TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(10)));
        var renewed = TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(90));

        await InstallAsync(client, "acme", Domain, renewed);

        Assert.Equal(1, ledger.Certificates.Count);
        Assert.True(ledger.Certificates.TryGet(Domain, out var record));
        Assert.NotNull(record);
        Assert.Equal(renewed.NotAfter, record.ExpiresAt);
    }

    /// <summary>The simulation has nothing to serve a key with, so it keeps none.</summary>
    [Fact]
    public async Task The_private_key_is_not_kept_anywhere_in_the_record()
    {
        var (ledger, client) = NewHost();
        var material = TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(90));
        await InstallAsync(client, "acme", Domain, material);

        Assert.True(ledger.Certificates.TryGet(Domain, out var record));
        Assert.NotNull(record);
        var keyBody = material.PrivateKeyPem.Split('\n')[1];
        var certificateBody = material.CertificatePem.Split('\n')[1];
        Assert.False(string.IsNullOrWhiteSpace(keyBody));
        Assert.DoesNotContain(keyBody, record.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(certificateBody, record.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A site that is not there, or belongs to another account, has nothing to install into.</summary>
    [Fact]
    public async Task Installing_for_an_unknown_site_or_another_accounts_site_reports_not_found()
    {
        var (ledger, client) = NewHost();
        SimulatedHosts.AddAccount(ledger, "globex", _clock.UtcNow);
        var material = TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(90));

        var unknown = await InstallAsync(client, "acme", "nothing.example.test", material);
        var foreign = await InstallAsync(client, "globex", Domain, material);

        Assert.Equal("AgentNotFound", unknown.Error!.Code);
        Assert.Equal("AgentNotFound", foreign.Error!.Code);
        Assert.Equal(0, ledger.Certificates.Count);
    }

    /// <summary>Text that is not a certificate is refused with the real agent's code.</summary>
    [Fact]
    public async Task Text_that_is_not_a_certificate_is_refused_as_unreadable()
    {
        var (_, client) = NewHost();
        var material = TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(90));

        var installed = await client.InstallCertificateAsync(
            "acme", Domain, "not a certificate", material.PrivateKeyPem, Descriptor, CancellationToken.None);

        Assert.Equal("AgentCertificateUnreadable", installed.Error!.Code);
    }

    /// <summary>Text that is not a private key is refused with the real agent's code.</summary>
    [Fact]
    public async Task Text_that_is_not_a_private_key_is_refused_as_unreadable()
    {
        var (_, client) = NewHost();
        var material = TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(90));

        var installed = await client.InstallCertificateAsync(
            "acme", Domain, material.CertificatePem, "not a key", Descriptor, CancellationToken.None);

        Assert.Equal("AgentPrivateKeyUnreadable", installed.Error!.Code);
    }

    /// <summary>A key that belongs to another certificate is refused as a mismatch, and nothing is installed.</summary>
    [Fact]
    public async Task A_key_that_belongs_to_another_certificate_is_refused_as_a_mismatch()
    {
        var (ledger, client) = NewHost();
        var material = TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(90));
        var other = TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(90));

        var installed = await client.InstallCertificateAsync(
            "acme", Domain, material.CertificatePem, other.PrivateKeyPem, Descriptor, CancellationToken.None);

        Assert.Equal("AgentPrivateKeyMismatch", installed.Error!.Code);
        Assert.Equal(0, ledger.Certificates.Count);
    }

    /// <summary>Material over its cap is refused before it is parsed.</summary>
    [Fact]
    public async Task Material_over_the_size_cap_is_refused_as_invalid_input()
    {
        var (_, client) = NewHost(new SimulatedAgentLimits { MaxCertificatePemBytes = 100 });
        var material = TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(90));

        var installed = await InstallAsync(client, "acme", Domain, material);

        Assert.Equal("AgentInvalidInput", installed.Error!.Code);
    }

    /// <summary>A certificate can be removed once, and only by the account that owns it.</summary>
    [Fact]
    public async Task Removing_a_certificate_works_once_and_only_for_its_owner()
    {
        var (ledger, client) = NewHost();
        SimulatedHosts.AddAccount(ledger, "globex", _clock.UtcNow);
        var before = await client.RemoveCertificateAsync("acme", Domain, Descriptor, CancellationToken.None);
        await InstallAsync(client, "acme", Domain, TestCertificates.EllipticCurve(_clock.UtcNow.AddDays(90)));

        var foreign = await client.RemoveCertificateAsync("globex", Domain, Descriptor, CancellationToken.None);
        var removed = await client.RemoveCertificateAsync("acme", Domain, Descriptor, CancellationToken.None);
        var again = await client.RemoveCertificateAsync("acme", Domain, Descriptor, CancellationToken.None);

        Assert.Equal("AgentNotFound", before.Error!.Code);
        Assert.Equal("AgentNotFound", foreign.Error!.Code);
        Assert.True(removed.IsSuccess);
        Assert.Equal("AgentNotFound", again.Error!.Code);
    }

    /// <summary>The panel's own certificate is replaced, and its expiry is the one in the new certificate.</summary>
    [Fact]
    public async Task Installing_the_panel_certificate_records_it_and_returns_its_expiry()
    {
        var (ledger, client) = NewHost();
        var material = TestCertificates.Rsa(_clock.UtcNow.AddDays(90));

        var installed = await client.InstallPanelCertificateAsync(
            material.CertificatePem, material.PrivateKeyPem, CancellationToken.None);

        Assert.Equal(material.NotAfter, installed.Value.ExpiresAt);
        var panel = ledger.PanelCertificate.Value;
        Assert.NotNull(panel);
        Assert.False(panel.SelfSigned);
        Assert.Equal(material.NotAfter, panel.ExpiresAt);
    }

    /// <summary>A refused panel certificate leaves the previous one in place, as the real agent restores it.</summary>
    [Fact]
    public async Task A_refused_panel_certificate_leaves_the_previous_one_in_place()
    {
        var (ledger, client) = NewHost();
        var good = TestCertificates.Rsa(_clock.UtcNow.AddDays(90));
        var other = TestCertificates.Rsa(_clock.UtcNow.AddDays(30));
        await client.InstallPanelCertificateAsync(good.CertificatePem, good.PrivateKeyPem, CancellationToken.None);

        var refused = await client.InstallPanelCertificateAsync(
            other.CertificatePem, good.PrivateKeyPem, CancellationToken.None);

        Assert.Equal("AgentPrivateKeyMismatch", refused.Error!.Code);
        Assert.Equal(good.NotAfter, ledger.PanelCertificate.Value!.ExpiresAt);
    }

    /// <summary>Restoring the placeholder gives a self-signed certificate valid for a year.</summary>
    [Fact]
    public async Task Restoring_the_self_signed_certificate_is_valid_for_a_year()
    {
        var (ledger, client) = NewHost();

        var restored = await client.RestorePanelSelfSignedAsync("panel.example.test", CancellationToken.None);

        Assert.Equal(_clock.UtcNow.AddDays(365), restored.Value.ExpiresAt);
        var panel = ledger.PanelCertificate.Value;
        Assert.NotNull(panel);
        Assert.True(panel.SelfSigned);
    }

    /// <summary>A host name that is not a host name is refused.</summary>
    [Fact]
    public async Task Restoring_the_self_signed_certificate_for_a_malformed_host_name_is_refused()
    {
        var (ledger, client) = NewHost();

        var restored = await client.RestorePanelSelfSignedAsync("Panel Example", CancellationToken.None);

        Assert.Equal("AgentInvalidInput", restored.Error!.Code);
        Assert.Null(ledger.PanelCertificate.Value);
    }

    /// <summary>A challenge is written, rewritten in place, removed, and then reported missing.</summary>
    [Fact]
    public async Task A_challenge_is_written_rewritten_removed_and_then_missing()
    {
        var (ledger, client) = NewHost();

        var written = await client.WritePanelChallengeAsync("tok-1_A", "tok-1_A.thumb", CancellationToken.None);
        var rewritten = await client.WritePanelChallengeAsync("tok-1_A", "tok-1_A.other", CancellationToken.None);
        Assert.True(ledger.PanelChallenges.TryGet("tok-1_A", out var challenge));
        Assert.NotNull(challenge);
        var removed = await client.RemovePanelChallengeAsync("tok-1_A", CancellationToken.None);
        var again = await client.RemovePanelChallengeAsync("tok-1_A", CancellationToken.None);

        Assert.True(written.IsSuccess);
        Assert.True(rewritten.IsSuccess);
        Assert.Equal("tok-1_A.other", challenge.KeyAuthorization);
        Assert.True(removed.IsSuccess);
        Assert.Equal("AgentNotFound", again.Error!.Code);
        Assert.Equal(0, ledger.PanelChallenges.Count);
    }

    /// <summary>A token that could name a path, or an empty authorization, is refused.</summary>
    /// <param name="token">The token in the request.</param>
    /// <param name="authorization">The key authorization in the request.</param>
    [Theory]
    [InlineData("../etc/passwd", "x")]
    [InlineData("a/b", "x")]
    [InlineData("", "x")]
    [InlineData("tok", "")]
    [InlineData("tok", "line\nbreak")]
    public async Task A_malformed_challenge_is_refused_as_invalid_input(string token, string authorization)
    {
        var (ledger, client) = NewHost();

        var written = await client.WritePanelChallengeAsync(token, authorization, CancellationToken.None);

        Assert.Equal("AgentInvalidInput", written.Error!.Code);
        Assert.Equal(0, ledger.PanelChallenges.Count);
    }

    /// <summary>A visitor cannot make the host hold more challenge files than the cap.</summary>
    [Fact]
    public async Task Writing_more_challenges_than_the_cap_reports_limit_reached()
    {
        var (_, client) = NewHost(new SimulatedAgentLimits { MaxPanelChallenges = 1 });
        await client.WritePanelChallengeAsync("one", "x", CancellationToken.None);

        var second = await client.WritePanelChallengeAsync("two", "x", CancellationToken.None);
        var rewrite = await client.WritePanelChallengeAsync("one", "y", CancellationToken.None);

        Assert.Equal("AgentLimitReached", second.Error!.Code);
        Assert.True(rewrite.IsSuccess);
    }

    /// <summary>Builds a host with one account, one static site, and a client over it.</summary>
    /// <param name="limits">The caps to build it with; the defaults when null.</param>
    /// <returns>The ledger and the client.</returns>
    private (SimulatedAgentLedger Ledger, SimulatedAgentSslClient Client) NewHost(
        SimulatedAgentLimits? limits = null)
    {
        var ledger = SimulatedHosts.NewLedger(limits);
        SimulatedHosts.AddAccount(ledger, SimulatedHosts.AccountName, _clock.UtcNow);
        ledger.Vhosts.Add(
            Domain,
            new SimulatedVhost(
                "acme",
                Domain,
                [],
                SiteBackendKind.Static,
                string.Empty,
                string.Empty,
                0,
                [],
                true,
                "/home/acme/sites/" + Domain));

        return (ledger, new SimulatedAgentSslClient(ledger, _clock));
    }

    /// <summary>Installs <paramref name="material"/> for a site.</summary>
    /// <param name="client">The client to call.</param>
    /// <param name="account">The owning account.</param>
    /// <param name="domain">The site's domain.</param>
    /// <param name="material">The certificate and key.</param>
    /// <returns>The client's answer.</returns>
    private static Task<Result<InstalledCertificateDto>> InstallAsync(
        SimulatedAgentSslClient client,
        string account,
        string domain,
        TestCertificateMaterial material)
    {
        return client.InstallCertificateAsync(
            account, domain, material.CertificatePem, material.PrivateKeyPem, Descriptor, CancellationToken.None);
    }
}
