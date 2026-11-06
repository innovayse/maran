using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of <see cref="ApiKeyAuthenticator"/>.</summary>
/// <remarks>
/// <para>
/// UNOBSERVED HERE: that the digest is computed for an unknown prefix. The digest is a static pure
/// function reached without a seam, so no test can count the calls without changing production code, and
/// a timing assertion would be flaky. What IS observed is the outcome: an unknown prefix answers with the
/// same <see cref="ApiKeyDenied.Instance"/> as every other refusal. The equal work is read in the
/// authenticator's source, as the threat note's reviewer list says.
/// </para>
/// </remarks>
public sealed class ApiKeyAuthenticatorTests : IDisposable
{
    private const string Address = "203.0.113.7";

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly string _database = Guid.NewGuid().ToString();
    private readonly IdentityDbContext _context;

    /// <summary>Opens the fixture's context over its own named in-memory database.</summary>
    public ApiKeyAuthenticatorTests()
    {
        _context = IdentityTestContext.CreateOver(_database);
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds the authenticator under test over a context.</summary>
    private static ApiKeyAuthenticator NewAuthenticator(IdentityDbContext context)
    {
        return new ApiKeyAuthenticator(context, NullLogger<ApiKeyAuthenticator>.Instance);
    }

    /// <summary>
    /// Authenticates over a context of its own, as a request does: a context shared with the seeding
    /// code would answer from its tracked rows and never see a change made elsewhere.
    /// </summary>
    private async Task<ApiKeyAuthentication> AuthenticateAsync(string header, string address = Address, DateTimeOffset? at = null)
    {
        using var context = IdentityTestContext.CreateOver(_database);

        return await NewAuthenticator(context).AuthenticateAsync(header, address, at ?? Now, CancellationToken.None);
    }

    /// <summary>Changes the last character of a key, which makes it a well-formed key with a wrong secret.</summary>
    private static string WithWrongSecret(string plaintext)
    {
        return plaintext[..^1] + (plaintext[^1] == 'A' ? 'B' : 'A');
    }

    /// <summary>A valid key authenticates with its own scopes plus the implicit self scope.</summary>
    [Fact]
    public async Task A_valid_key_authenticates_with_its_scopes_and_the_implicit_self_scope()
    {
        var (key, plaintext) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), scopes: [ApiKeyScopes.ServerRead]);

        var outcome = await AuthenticateAsync(plaintext);

        var authenticated = Assert.IsType<ApiKeyAuthenticated>(outcome);
        Assert.Equal(key.Id, authenticated.KeyId);
        Assert.Equal([ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead], authenticated.Scopes);
    }

    /// <summary>A key created with no scope beyond the implicit one authenticates with the self scope alone.</summary>
    [Fact]
    public async Task A_key_with_only_the_self_scope_authenticates_with_that_scope_alone()
    {
        var (_, plaintext) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));

        var authenticated = Assert.IsType<ApiKeyAuthenticated>(await AuthenticateAsync(plaintext));

        Assert.Equal([ApiKeyScopes.SelfRead], authenticated.Scopes);
    }

    /// <summary>Malformed, unknown-prefix, wrong-secret, revoked, expired and out-of-range keys all answer with the one shared denial.</summary>
    [Fact]
    public async Task Every_refusal_reason_answers_with_the_same_denial()
    {
        var (_, valid) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "valid");
        var (_, revoked) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "revoked", revoked: true);
        var (_, expired) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-2), name: "expired", expiresAt: Now.AddDays(-1));
        var (_, bound) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "bound", allowedCidrs: ["198.51.100.0/24"]);
        var (unknownPlaintext, _, _) = ApiKeyHasher.Mint();
        var wrongSecret = WithWrongSecret(valid);

        var outcomes = new[]
        {
            await AuthenticateAsync("not a key"),
            await AuthenticateAsync(string.Empty),
            await AuthenticateAsync(unknownPlaintext),
            await AuthenticateAsync(wrongSecret),
            await AuthenticateAsync(revoked),
            await AuthenticateAsync(expired),
            await AuthenticateAsync(bound),
        };

        Assert.All(outcomes, outcome =>
        {
            Assert.Same(ApiKeyDenied.Instance, outcome);
        });
        Assert.IsType<ApiKeyAuthenticated>(await AuthenticateAsync(valid));
    }

    /// <summary>A key bound to a range is accepted from inside it, from its edges, and refused from just outside.</summary>
    [Fact]
    public async Task A_bound_key_is_accepted_inside_its_range_and_refused_outside()
    {
        var (_, plaintext) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), allowedCidrs: ["198.51.100.0/24", "2001:db8::/32"]);

        Assert.IsType<ApiKeyAuthenticated>(await AuthenticateAsync(plaintext, "198.51.100.0"));
        Assert.IsType<ApiKeyAuthenticated>(await AuthenticateAsync(plaintext, "198.51.100.255"));
        Assert.IsType<ApiKeyAuthenticated>(await AuthenticateAsync(plaintext, "2001:db8::1"));
        Assert.IsType<ApiKeyAuthenticated>(await AuthenticateAsync(plaintext, "::ffff:198.51.100.9"));
        Assert.Same(ApiKeyDenied.Instance, await AuthenticateAsync(plaintext, "198.51.101.0"));
        Assert.Same(ApiKeyDenied.Instance, await AuthenticateAsync(plaintext, "2001:db9::1"));
    }

    /// <summary>A bound key is refused when the caller's address is unknown or unreadable, while an unbound key accepts any address.</summary>
    [Fact]
    public async Task A_bound_key_is_refused_for_an_unreadable_address_and_an_unbound_key_is_not()
    {
        var (_, bound) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "bound", allowedCidrs: ["198.51.100.0/24"]);
        var (_, unbound) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "unbound");

        Assert.Same(ApiKeyDenied.Instance, await AuthenticateAsync(bound, "unknown"));
        Assert.Same(ApiKeyDenied.Instance, await AuthenticateAsync(bound, string.Empty));
        Assert.IsType<ApiKeyAuthenticated>(await AuthenticateAsync(unbound, "unknown"));
    }

    /// <summary>A key is refused from the instant it expires, and accepted a tick before.</summary>
    [Fact]
    public async Task A_key_is_refused_from_the_instant_it_expires()
    {
        var (_, plaintext) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), expiresAt: Now.AddHours(1));

        Assert.IsType<ApiKeyAuthenticated>(await AuthenticateAsync(plaintext, at: Now.AddHours(1).AddTicks(-1)));
        Assert.Same(ApiKeyDenied.Instance, await AuthenticateAsync(plaintext, at: Now.AddHours(1)));
    }

    /// <summary>A key revoked after it authenticated is refused on the very next call, because nothing is cached.</summary>
    [Fact]
    public async Task A_key_revoked_after_authenticating_is_refused_on_the_next_call()
    {
        var (key, plaintext) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));
        Assert.IsType<ApiKeyAuthenticated>(await AuthenticateAsync(plaintext));

        using var other = IdentityTestContext.CreateOver(_database);
        (await other.ApiKeys.SingleAsync(row => row.Id == key.Id)).Revoke(Now);
        await other.SaveChangesAsync();

        Assert.Same(ApiKeyDenied.Instance, await AuthenticateAsync(plaintext));
    }

    /// <summary>A refused call never records a use on any key.</summary>
    [Fact]
    public async Task A_refused_call_records_no_use()
    {
        var (key, plaintext) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), allowedCidrs: ["198.51.100.0/24"]);

        await AuthenticateAsync(plaintext, "203.0.113.7");
        await AuthenticateAsync(WithWrongSecret(plaintext));

        Assert.Null((await _context.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == key.Id)).LastUsedAt);
    }

    /// <summary>The last use is recorded on the first call and then at most once a minute.</summary>
    [Fact]
    public async Task The_last_use_is_recorded_on_the_first_call_and_then_once_a_minute()
    {
        var (key, plaintext) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));

        await AuthenticateAsync(plaintext, "203.0.113.7", Now);
        await AuthenticateAsync(plaintext, "203.0.113.8", Now.AddSeconds(59));
        var afterBurst = await _context.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == key.Id);

        await AuthenticateAsync(plaintext, "203.0.113.9", Now.AddSeconds(60));
        var afterMinute = await _context.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == key.Id);

        Assert.Equal(Now, afterBurst.LastUsedAt);
        Assert.Equal("203.0.113.7", afterBurst.LastUsedIp);
        Assert.Equal(Now.AddSeconds(60), afterMinute.LastUsedAt);
        Assert.Equal("203.0.113.9", afterMinute.LastUsedIp);
    }

    /// <summary>A failed save of the last use does not fail authentication and leaves no stale stamp behind.</summary>
    [Fact]
    public async Task A_failed_last_use_save_does_not_fail_authentication()
    {
        var (key, plaintext) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));
        using var failing = IdentityTestContext.CreateOver(_database, new UniqueViolationOnSaveInterceptor());

        var outcome = await NewAuthenticator(failing).AuthenticateAsync(plaintext, Address, Now, CancellationToken.None);

        var authenticated = Assert.IsType<ApiKeyAuthenticated>(outcome);
        Assert.Equal(key.Id, authenticated.KeyId);
        Assert.Null((await _context.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == key.Id)).LastUsedAt);
    }
}
