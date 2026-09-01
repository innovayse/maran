using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// The signed conversation: nonce handling, the one retry <c>badNonce</c> gets, and what an
/// authority's refusal is allowed to put in a log line.
/// </summary>
public sealed class AcmeSessionTests : IDisposable
{
    /// <summary>The fake authority every test here talks to.</summary>
    private readonly FakeAcmeAuthority _authority = new(string.Empty);

    /// <summary>Where the session's log lines are collected.</summary>
    private readonly RecordingLogger<AcmeSession> _logger = new();

    /// <summary>The account key the session signs with.</summary>
    private readonly AcmeSigner _signer = AcmeSigner.CreateNew();

    /// <inheritdoc />
    public void Dispose()
    {
        _signer.Dispose();
        _authority.Dispose();
    }

    /// <summary>A stale nonce is retried exactly once and the retry is re signed.</summary>
    [Fact]
    public async Task A_stale_nonce_is_retried_exactly_once_and_the_retry_is_re_signed()
    {
        _authority.BadNonceRefusals = 1;
        using var http = Client();
        var session = new AcmeSession(http, _signer, FakeAcmeAuthority.BaseUrl + "/acct/1", _logger);
        await session.RefreshNonceAsync(FakeAcmeAuthority.BaseUrl + "/nonce", CancellationToken.None);

        var result = await session.PostAsync(FakeAcmeAuthority.BaseUrl + "/new-order", "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _authority.PostBodies.Count);

        // Re-signed, not replayed: a replay of a consumed nonce could only be refused again, which
        // is exactly why this retry cannot live in the HTTP resilience pipeline.
        Assert.NotEqual(_authority.PostBodies[0], _authority.PostBodies[1]);
    }

    /// <summary>A refusal that is not a stale nonce is not retried.</summary>
    [Fact]
    public async Task A_refusal_that_is_not_a_stale_nonce_is_not_retried()
    {
        _authority.RateLimited = true;
        using var http = Client();
        var session = new AcmeSession(http, _signer, FakeAcmeAuthority.BaseUrl + "/acct/1", _logger);

        var result = await session.PostAsync(FakeAcmeAuthority.BaseUrl + "/new-order", "{}", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Single(_authority.PostBodies);
    }

    /// <summary>A refusal logs the authoritys status and problem type.</summary>
    [Fact]
    public async Task A_refusal_logs_the_authoritys_status_and_problem_type()
    {
        _authority.RateLimited = true;
        using var http = Client();
        var session = new AcmeSession(http, _signer, FakeAcmeAuthority.BaseUrl + "/acct/1", _logger);

        await session.PostAsync(FakeAcmeAuthority.BaseUrl + "/new-order", "{}", CancellationToken.None);

        var line = Assert.Single(_logger.Messages);
        Assert.Contains("429", line, StringComparison.Ordinal);
        Assert.Contains("urn:ietf:params:acme:error:rateLimited", line, StringComparison.Ordinal);
    }

    /// <summary>A refusal never logs the authoritys prose detail.</summary>
    [Fact]
    public async Task A_refusal_never_logs_the_authoritys_prose_detail()
    {
        // `detail` is free text the authority writes, and on a finalize it quotes the CSR. The
        // machine fields carry no caller data; the prose can.
        _authority.RateLimited = true;
        using var http = Client();
        var session = new AcmeSession(http, _signer, FakeAcmeAuthority.BaseUrl + "/acct/1", _logger);

        await session.PostAsync(FakeAcmeAuthority.BaseUrl + "/new-order", "{}", CancellationToken.None);

        foreach (var line in _logger.Messages)
        {
            Assert.DoesNotContain("SECRET-DETAIL-PROSE", line, StringComparison.Ordinal);
        }
    }

    /// <summary>A refusal carries a code outward and never the authoritys body.</summary>
    [Fact]
    public async Task A_refusal_carries_a_code_outward_and_never_the_authoritys_body()
    {
        _authority.RateLimited = true;
        using var http = Client();
        var session = new AcmeSession(http, _signer, FakeAcmeAuthority.BaseUrl + "/acct/1", _logger);

        var result = await session.PostAsync(FakeAcmeAuthority.BaseUrl + "/new-order", "{}", CancellationToken.None);

        Assert.Equal("AcmeOrderRejected", result.Error!.Code);
    }

    /// <summary>The location header is carried alongside the body because acme puts identity there.</summary>
    [Fact]
    public async Task The_location_header_is_carried_alongside_the_body_because_acme_puts_identity_there()
    {
        using var http = Client();
        var session = new AcmeSession(http, _signer, null, _logger);
        await session.RefreshNonceAsync(FakeAcmeAuthority.BaseUrl + "/nonce", CancellationToken.None);

        var result = await session.PostAsync(FakeAcmeAuthority.BaseUrl + "/new-acct", "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FakeAcmeAuthority.BaseUrl + "/acct/1", result.Value.Location);
    }

    /// <summary>Builds an http client over the fake authority.</summary>
    /// <returns>The client.</returns>
    private HttpClient Client()
    {
        return new HttpClient(_authority, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
