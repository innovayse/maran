using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Common.Options;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// The ACME order sequence, end to end, against a fake authority: directory → nonce → account →
/// order → authorization → challenge → finalize → download.
/// </summary>
/// <remarks>
/// These are the tests whose absence let a broken renewal path ship. Every handler test in this
/// suite replaces <c>IAcmeClient</c> wholesale, so nothing above this file can see what the client
/// actually says to an authority — including the case that matters most, an order for a domain the
/// account has already validated.
/// </remarks>
public sealed class AcmeClientTests : IDisposable
{
    /// <summary>When the certificate the fake authority issues expires.</summary>
    private static readonly DateTimeOffset Expiry = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The fake authority under test.</summary>
    private readonly FakeAcmeAuthority _authority = new(SelfSignedCertificate.PemExpiringAt(Expiry));

    /// <summary>Records what the panel asked the agent to write into the document root.</summary>
    private readonly RecordingAgentFilesClient _files = new();

    /// <summary>The context the account registration is stored in.</summary>
    private readonly SslDbContext _dbContext =
        SslTestContext.Create(FakeCurrentUser.Admin());

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
        _authority.Dispose();
    }

    /// <summary>A pending order runs the whole challenge sequence and returns the issued material.</summary>
    [Fact]
    public async Task A_pending_order_runs_the_whole_challenge_sequence_and_returns_the_issued_material()
    {
        var result = await OrderAsync();

        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.Error!.Code + " :: " + string.Join(" | ", _authority.Requests));
        Assert.Equal(Expiry, result.Value.NotAfter);
        Assert.Contains("BEGIN CERTIFICATE", result.Value.CertificatePem, StringComparison.Ordinal);
        Assert.Contains("PRIVATE KEY", result.Value.PrivateKeyPem, StringComparison.Ordinal);
        Assert.Equal(1, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/chall/1"));
    }

    /// <summary>The challenge file is written and then removed again.</summary>
    [Fact]
    public async Task The_challenge_file_is_written_and_then_removed_again()
    {
        await OrderAsync();

        var write = Assert.Single(_files.Writes);
        Assert.Equal("sites/example.com/.well-known/acme-challenge/tok3n", write.Path);

        // Pinned because a cleanup that silently stopped happening leaves a stale token under
        // .well-known on every site the panel ever issued for.
        var delete = Assert.Single(_files.Deletes);
        Assert.Equal(write.Path, delete.Path);
    }

    /// <summary>An order the authority already reports ready skips the challenge entirely.</summary>
    [Fact]
    public async Task An_order_the_authority_already_reports_ready_skips_the_challenge_entirely()
    {
        // The renewal case. Authorities cache authorizations — Let's Encrypt for thirty days — so a
        // re-order comes back "ready" with nothing to prove. Answering a challenge on it is refused
        // as "malformed", which is how renewal of a recently-issued domain used to fail outright.
        _authority.OrderStatus = "ready";

        var result = await OrderAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/chall/1"));
        Assert.Equal(0, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/authz/1"));
        Assert.Empty(_files.Writes);
    }

    /// <summary>An authorization that is already valid skips the challenge even on a pending order.</summary>
    [Fact]
    public async Task An_authorization_that_is_already_valid_skips_the_challenge_even_on_a_pending_order()
    {
        _authority.AuthorizationStatus = "valid";

        var result = await OrderAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/chall/1"));
        Assert.Empty(_files.Writes);
    }

    /// <summary>A stale nonce is retried once by re signing rather than by replaying the body.</summary>
    [Fact]
    public async Task A_stale_nonce_is_retried_once_by_re_signing_rather_than_by_replaying_the_body()
    {
        _authority.BadNonceRefusals = 1;

        var result = await OrderAsync();

        Assert.True(result.IsSuccess);

        // Two DIFFERENT bodies for the refused call: the retry re-signs with the nonce the refusal
        // handed out. A replay of the same body could only be refused again.
        Assert.Equal(_authority.PostBodies.Count, _authority.PostBodies.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A signed post that fails with a server error is never replayed.</summary>
    [Fact]
    public async Task A_signed_post_that_fails_with_a_server_error_is_never_replayed()
    {
        // The rate-limit hazard: a 5xx on newOrder may follow an order the authority already created,
        // so a replay creates a second one. Nothing in this client retries a signed POST.
        _authority.SignedPostServerFailures = 1;

        var result = await OrderAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(1, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/new-acct"));
    }

    /// <summary>The unsigned directory fetch is retried once because replaying it costs nothing.</summary>
    [Fact]
    public async Task The_unsigned_directory_fetch_is_retried_once_because_replaying_it_costs_nothing()
    {
        _authority.DirectoryTransportFailures = 1;

        var result = await OrderAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _authority.CountOf(FakeAcmeAuthority.DirectoryUrl));
    }

    /// <summary>An unreachable authority is a typed failure rather than an exception.</summary>
    [Fact]
    public async Task An_unreachable_authority_is_a_typed_failure_rather_than_an_exception()
    {
        _authority.DirectoryTransportFailures = 5;

        var result = await OrderAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("AcmeAuthorityUnreachable", result.Error!.Code);
    }

    /// <summary>A refused order returns a code and never the authoritys prose.</summary>
    [Fact]
    public async Task A_refused_order_returns_a_code_and_never_the_authoritys_prose()
    {
        _authority.RateLimited = true;

        var result = await OrderAsync();

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("SECRET-DETAIL-PROSE", result.Error!.Code, StringComparison.Ordinal);
    }

    /// <summary>The client asks the factory for the named governed acme client.</summary>
    [Fact]
    public async Task The_client_asks_the_factory_for_the_named_governed_acme_client()
    {
        var factory = new StubHttpClientFactory(_authority);
        await ClientFor(factory).OrderAsync(new AcmeOrderRequest("example.com", "acct"), CancellationToken.None);

        // A client built any other way carries no timeout at all.
        Assert.Equal([AcmeOptions.HttpClientName], factory.RequestedNames);
    }

    /// <summary>The account registration is stored once and reused by a second order.</summary>
    [Fact]
    public async Task The_account_registration_is_stored_once_and_reused_by_a_second_order()
    {
        // Registering afresh per order spends the authority's new-account budget and throws away the
        // cached authorizations that make renewal cheap.
        await OrderAsync();
        await OrderAsync();

        Assert.Equal(1, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/new-acct"));
        Assert.Equal(1, await _dbContext.AcmeAccounts.CountAsync());
    }

    /// <summary>Runs one order through the production entry point.</summary>
    /// <returns>The order's result.</returns>
    private async Task<Result<IssuedCertificate>> OrderAsync()
    {
        return await ClientFor(new StubHttpClientFactory(_authority))
            .OrderAsync(new AcmeOrderRequest("example.com", "acct"), CancellationToken.None);
    }

    /// <summary>Builds the production client over this class's doubles.</summary>
    /// <param name="factory">The HTTP client factory to hand it.</param>
    /// <returns>The client under test.</returns>
    private AcmeClient ClientFor(IHttpClientFactory factory)
    {
        var options = Options.Create(new AcmeOptions
        {
            DirectoryUrl = FakeAcmeAuthority.DirectoryUrl,
            ContactEmail = "ops@example.com",

            // Bounded tightly on purpose. The poll loops close over AdvancingClock, which moves a
            // second per read, so thirty seconds of budget is about thirty polls — enough for the
            // fake authority to turn the authorization valid on the first one, and few enough that a
            // loop which stopped terminating fails the test in seconds instead of hanging it.
            ValidationTimeoutSeconds = 30,
            PollIntervalSeconds = 1,
        });

        return new AcmeClient(
            factory,
            options,
            new AcmeChallengeWriter(_files),
            new AcmeAccountStore(_dbContext, new FakeClock(Expiry), NullLogger<AcmeAccountStore>.Instance),
            new AdvancingClock(Expiry),
            NullLogger<AcmeClient>.Instance);
    }
}
