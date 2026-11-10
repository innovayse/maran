using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Options;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// The wildcard order, end to end against the fake authority: two identifiers, two authorizations, a TXT record
/// per authorization published before either challenge is answered, and every record removed again on every way
/// the order can end. The proof of control is a record in a zone, so a double stands in for the DNS module's
/// door and records what was done to it.
/// </summary>
public sealed class AcmeClientWildcardTests : IDisposable
{
    /// <summary>When the certificate the fake authority issues expires.</summary>
    private static readonly DateTimeOffset Expiry = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The account whose zone answers.</summary>
    private static readonly Guid AccountId = new("66666666-6666-4666-8666-666666666666");

    /// <summary>The domain every order is for.</summary>
    private const string Domain = "example.com";

    /// <summary>A fake authority that offers DNS-01 on two authorizations, as a wildcard order gets.</summary>
    private readonly FakeAcmeAuthority _authority =
        new(SelfSignedCertificate.PemExpiringAt(Expiry)) { ChallengeType = "dns-01", AuthorizationCount = 2 };

    /// <summary>Records what the panel asked the agent to write into the document root; a wildcard order writes none.</summary>
    private readonly RecordingAgentFilesClient _files = new();

    /// <summary>The DNS module's door, as the certificate module sees it.</summary>
    private readonly RecordingDnsChallengeRecords _dns = new();

    /// <summary>Everything the client logged.</summary>
    private readonly RecordingLogger<AcmeClient> _logger = new();

    /// <summary>The context the account registration is stored in.</summary>
    private readonly SslDbContext _dbContext = SslTestContext.Create(FakeCurrentUser.Admin());

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
        _authority.Dispose();
    }

    /// <summary>The order names the domain and its wildcard, and the signing request carries both names.</summary>
    [Fact]
    public async Task The_order_names_the_domain_and_its_wildcard_and_the_signing_request_carries_both()
    {
        var result = await OrderAsync();

        Assert.True(result.IsSuccess, result.IsSuccess ? string.Empty : result.Error!.Code);
        Assert.Equal([Domain, "*." + Domain], _authority.OrderedIdentifiers());
        var request = CertificateRequest.LoadSigningRequest(
            _authority.FinalizedCsr(), HashAlgorithmName.SHA256, CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
        var names = request.CertificateExtensions.OfType<X509SubjectAlternativeNameExtension>()
            .SelectMany(extension =>
            {
                return extension.EnumerateDnsNames();
            })
            .ToList();
        Assert.Equal([Domain, "*." + Domain], names);
    }

    /// <summary>Each authorization gets a record whose value is the independently computed digest, and each challenge is answered.</summary>
    [Fact]
    public async Task Each_authorization_gets_a_record_with_the_independently_computed_digest()
    {
        var result = await OrderAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _dns.Published.Count);
        Assert.All(_dns.Published, record =>
        {
            Assert.Equal(Domain, record.Hostname);
            Assert.Equal(AccountId, record.AccountId);
            Assert.Equal(43, record.Value.Length);
        });

        // The expected values are worked out here from the account's public key and the tokens the authority
        // offered, so a client that published the unhashed key authorization, or one digest twice, fails.
        Assert.Equal(
            new[] { _authority.ExpectedTxtValue(1), _authority.ExpectedTxtValue(2) }.Order(StringComparer.Ordinal),
            _dns.Published.Select(record =>
            {
                return record.Value;
            }).Order(StringComparer.Ordinal));
        Assert.Equal(1, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/chall/1"));
        Assert.Equal(1, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/chall/2"));
        Assert.Empty(_files.Writes);
    }

    /// <summary>Both records are published before the first challenge is answered.</summary>
    [Fact]
    public async Task Both_records_are_published_before_the_first_challenge_is_answered()
    {
        var challengesAnsweredWhenPublished = new List<int>();
        _dns.AfterPublish = _ =>
        {
            challengesAnsweredWhenPublished.Add(
                _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/chall/1") + _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/chall/2"));
        };

        await OrderAsync();

        Assert.Equal([0, 0], challengesAnsweredWhenPublished);
    }

    /// <summary>Every record is removed after a successful order.</summary>
    [Fact]
    public async Task Every_record_is_removed_after_a_successful_order()
    {
        var result = await OrderAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(
            _dns.Published.Select(record =>
            {
                return record.Handle;
            }),
            _dns.RemovalsAsked);
    }

    /// <summary>Every record is removed when the authority judges the challenge invalid, and the failure is worded by its type.</summary>
    [Fact]
    public async Task Every_record_is_removed_when_the_authority_judges_the_challenge_invalid()
    {
        _authority.FailsValidation = true;
        _authority.InvalidProblemType = "urn:ietf:params:acme:error:dns";

        var result = await OrderAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("AcmeDnsChallengeFailed", result.Error!.Code);
        Assert.Equal(ErrorType.UpstreamRefused, result.Error.Type);
        Assert.Equal(2, _dns.Published.Count);
        Assert.Equal(2, _dns.RemovalsAsked.Count);
    }

    /// <summary>A problem of another type is worded as a plain validation failure, which keeps the mapping honest.</summary>
    [Fact]
    public async Task A_problem_of_another_type_is_worded_as_a_plain_validation_failure()
    {
        _authority.FailsValidation = true;
        _authority.InvalidProblemType = "urn:ietf:params:acme:error:rejectedIdentifier";

        var result = await OrderAsync();

        Assert.Equal("AcmeValidationFailed", result.Error!.Code);
    }

    /// <summary>CAA and unauthorized problems are worded like a DNS problem.</summary>
    [Theory]
    [InlineData("urn:ietf:params:acme:error:caa")]
    [InlineData("urn:ietf:params:acme:error:unauthorized")]
    public async Task A_caa_or_unauthorized_problem_is_worded_like_a_dns_problem(string problemType)
    {
        _authority.FailsValidation = true;
        _authority.InvalidProblemType = problemType;

        var result = await OrderAsync();

        Assert.Equal("AcmeDnsChallengeFailed", result.Error!.Code);
    }

    /// <summary>A problem document that carries a PEM block never reaches the returned error or the log.</summary>
    [Fact]
    public async Task A_problem_document_carrying_a_pem_block_never_reaches_the_error_or_the_log()
    {
        const string Pem = "-----BEGIN PRIVATE KEY-----\nMIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0=\n-----END PRIVATE KEY-----";
        _authority.FailsValidation = true;
        _authority.InvalidProblemType = "urn:ietf:params:acme:error:dns";
        _authority.InvalidProblemDetail = Pem;

        var result = await OrderAsync();

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain("PRIVATE KEY", result.Error!.Code, StringComparison.Ordinal);
        Assert.DoesNotContain(_logger.Messages, message =>
        {
            return message.Contains("PRIVATE KEY", StringComparison.Ordinal)
                || message.Contains("MIGHAgEAMBMG", StringComparison.Ordinal);
        });

        // The inverse control: the type IS logged, so the log is not simply empty.
        Assert.Contains(_logger.Messages, message =>
        {
            return message.Contains("urn:ietf:params:acme:error:dns", StringComparison.Ordinal);
        });
    }

    /// <summary>A cancelled order still removes the records it published, with a token the caller cannot cancel.</summary>
    [Fact]
    public async Task A_cancelled_order_still_removes_the_records_it_published()
    {
        using var cancellation = new CancellationTokenSource();
        _dns.AfterPublish = count =>
        {
            if (count == 1)
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await OrderAsync(cancellation.Token);
        });

        var published = Assert.Single(_dns.Published);
        Assert.Equal(published.Handle, Assert.Single(_dns.RemovalsAsked));
        Assert.False(Assert.Single(_dns.RemovalTokens).CanBeCanceled);
    }

    /// <summary>A removal that fails, or throws, never turns a successful order into a failure.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_removal_that_fails_or_throws_never_fails_a_successful_order(bool throws)
    {
        _dns.RemoveThrows = throws;
        _dns.RemoveFailure = throws ? null : Error.Of("AgentUnavailable", ErrorType.Unavailable);

        var result = await OrderAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _dns.RemovalsAsked.Count);
    }

    /// <summary>An order the authority reports ready publishes nothing and answers no challenge.</summary>
    [Fact]
    public async Task An_order_the_authority_reports_ready_publishes_nothing()
    {
        _authority.OrderStatus = "ready";

        var result = await OrderAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(_dns.Published);
        Assert.Empty(_dns.RemovalsAsked);
        Assert.Equal(0, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/chall/1"));
    }

    /// <summary>Authorizations that are already valid on a pending order publish nothing.</summary>
    [Fact]
    public async Task Authorizations_that_are_already_valid_publish_nothing()
    {
        _authority.AuthorizationStatus = "valid";

        var result = await OrderAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(_dns.Published);
    }

    /// <summary>A zone this server does not serve fails the order before any challenge is answered.</summary>
    [Fact]
    public async Task A_zone_this_server_does_not_serve_fails_the_order_before_any_challenge_is_answered()
    {
        _dns.PublishFailure = Error.Of("DnsChallengeZoneNotFound", ErrorType.NotFound);

        var result = await OrderAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("WildcardZoneNotServed", result.Error!.Code);
        Assert.Equal(0, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/chall/1"));
        Assert.Empty(_dns.Published);
    }

    /// <summary>An authority that offers no DNS-01 challenge for a wildcard order is reported, and nothing is published.</summary>
    [Fact]
    public async Task An_authority_that_offers_no_dns_challenge_is_reported_and_nothing_is_published()
    {
        _authority.ChallengeType = "http-01";

        var result = await OrderAsync();

        Assert.Equal("AcmeChallengeUnavailable", result.Error!.Code);
        Assert.Empty(_dns.Published);
    }

    /// <summary>A plain order stays on HTTP-01, names one identifier and never touches the zone.</summary>
    [Fact]
    public async Task A_plain_order_stays_on_http_01_and_never_touches_the_zone()
    {
        using var plain = new FakeAcmeAuthority(SelfSignedCertificate.PemExpiringAt(Expiry));

        var result = await ClientFor(new StubHttpClientFactory(plain))
            .OrderAsync(new AcmeOrderRequest(Domain, new SiteDocumentRootChallenge("acct")), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([Domain], plain.OrderedIdentifiers());
        Assert.Single(_files.Writes);
        Assert.Empty(_dns.Published);
        Assert.Empty(_dns.Checked);
    }

    /// <summary>Runs one wildcard order through the production entry point.</summary>
    /// <param name="cancellationToken">Cancellation for the whole order.</param>
    /// <returns>The order's result.</returns>
    private async Task<Result<IssuedCertificate>> OrderAsync(CancellationToken cancellationToken = default)
    {
        return await ClientFor(new StubHttpClientFactory(_authority))
            .OrderAsync(new AcmeOrderRequest(Domain, new DnsZoneChallenge(AccountId)), cancellationToken);
    }

    /// <summary>Builds the production client over this class's doubles.</summary>
    /// <param name="factory">The HTTP client factory to hand it.</param>
    /// <returns>The client under test.</returns>
    private AcmeClient ClientFor(IHttpClientFactory factory)
    {
        var options = new OptionsWrapper<AcmeOptions>(new AcmeOptions
        {
            DirectoryUrl = FakeAcmeAuthority.DirectoryUrl,
            ContactEmail = "ops@example.com",
            ValidationTimeoutSeconds = 30,
            PollIntervalSeconds = 1,
        });

        return new AcmeClient(
            factory,
            options,
            new AcmeChallengeWriter(_files, new RecordingAgentSslClient()),
            new AcmeDnsChallengeWriter([_dns]),
            new AcmeAccountStore(_dbContext, new FakeClock(Expiry), NullLogger<AcmeAccountStore>.Instance),
            new AdvancingClock(Expiry),
            _logger);
    }
}
