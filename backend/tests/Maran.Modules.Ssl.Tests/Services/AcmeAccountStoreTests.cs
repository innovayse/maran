using System.Text.Json;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// The panel's registration with an authority: created once, reused for ever, and never left behind
/// when the authority refuses it.
/// </summary>
/// <remarks>
/// Reuse is the property worth pinning. Registering afresh on every order spends the authority's
/// new-account budget and discards the cached authorizations that make renewal cheap — and both
/// failures are invisible from the outside, because a client that re-registers still issues
/// certificates right up until the account limit stops it.
/// </remarks>
public sealed class AcmeAccountStoreTests : IDisposable
{
    /// <summary>A fixed instant, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The fake authority every test here registers with.</summary>
    private readonly FakeAcmeAuthority _authority = new(string.Empty);

    /// <summary>The context registrations are stored in.</summary>
    private readonly SslDbContext _dbContext = SslTestContext.Create(FakeCurrentUser.Admin());

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
        _authority.Dispose();
    }

    /// <summary>A first registration is created at the authority and stored.</summary>
    [Fact]
    public async Task A_first_registration_is_created_at_the_authority_and_stored()
    {
        var result = await GetOrCreateAsync();

        Assert.True(result.IsSuccess);
        result.Value.Signer.Dispose();

        var stored = await _dbContext.AcmeAccounts.SingleAsync();
        Assert.Equal(FakeAcmeAuthority.DirectoryUrl, stored.DirectoryUrl);
        Assert.Equal(FakeAcmeAuthority.BaseUrl + "/acct/1", stored.AccountUrl);
        Assert.Contains("PRIVATE KEY", stored.PrivateKeyPem, StringComparison.Ordinal);
    }

    /// <summary>A second call reuses the stored registration and its key rather than registering again.</summary>
    [Fact]
    public async Task A_second_call_reuses_the_stored_registration_and_its_key_rather_than_registering_again()
    {
        var first = await GetOrCreateAsync();
        var firstThumbprint = first.Value.Signer.JwkThumbprint();
        first.Value.Signer.Dispose();

        var second = await GetOrCreateAsync();
        var secondThumbprint = second.Value.Signer.JwkThumbprint();
        second.Value.Signer.Dispose();

        Assert.Equal(1, _authority.CountOf(FakeAcmeAuthority.BaseUrl + "/new-acct"));
        Assert.Equal(1, await _dbContext.AcmeAccounts.CountAsync());

        // The same KEY, not merely the same URL: the account is only usable with the key it was
        // registered under, and the thumbprint is what every HTTP-01 key authorization is built from.
        Assert.Equal(firstThumbprint, secondThumbprint);
    }

    /// <summary>A refused registration stores nothing and answers with a code.</summary>
    [Fact]
    public async Task A_refused_registration_stores_nothing_and_answers_with_a_code()
    {
        _authority.RateLimited = true;

        var result = await GetOrCreateAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("AcmeAccountRejected", result.Error!.Code);
        Assert.Empty(await _dbContext.AcmeAccounts.ToListAsync());
    }

    /// <summary>A directory the authority did not answer is a typed failure rather than a null reference.</summary>
    [Fact]
    public async Task A_directory_the_authority_did_not_answer_is_a_typed_failure_rather_than_a_null_reference()
    {
        using var http = Client();

        var result = await StoreFor().GetOrCreateAsync(
            http, directory: null, FakeAcmeAuthority.DirectoryUrl, "ops@example.com", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AcmeAuthorityUnreachable", result.Error!.Code);
    }

    /// <summary>Runs the production entry point against the fake authority's directory.</summary>
    /// <returns>The registration result.</returns>
    private async Task<Result<AcmeRegistration>> GetOrCreateAsync()
    {
        using var http = Client();
        var directory = JsonSerializer.Deserialize<JsonElement>(
            $$"""{"newNonce":"{{FakeAcmeAuthority.BaseUrl}}/nonce","newAccount":"{{FakeAcmeAuthority.BaseUrl}}/new-acct"}""");

        return await StoreFor().GetOrCreateAsync(
            http, directory, FakeAcmeAuthority.DirectoryUrl, "ops@example.com", CancellationToken.None);
    }

    /// <summary>Builds the store under test.</summary>
    /// <returns>The store.</returns>
    private AcmeAccountStore StoreFor()
    {
        return new AcmeAccountStore(_dbContext, new FakeClock(Now), NullLogger<AcmeAccountStore>.Instance);
    }

    /// <summary>Builds an http client over the fake authority.</summary>
    /// <returns>The client.</returns>
    private HttpClient Client()
    {
        return new HttpClient(_authority, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
