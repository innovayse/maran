using Maran.Modules.Identity.Commands.CreateApiKey;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Tests.Commands.CreateApiKey;

/// <summary>Behavioural contract of <see cref="CreateApiKeyCommandHandler"/>.</summary>
public sealed class CreateApiKeyCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);
    private readonly RecordingAuditWriter _audit = new();
    private readonly RecordingApiKeyCreationLock _lock = new();
    private readonly Guid _administrator = Guid.NewGuid();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds the handler under test over the fixture's context, clock and journal.</summary>
    private CreateApiKeyCommandHandler NewHandler()
    {
        return new CreateApiKeyCommandHandler(_context, _clock, new IdentityAuditJournal(_audit, new StubCurrentUser()), _lock, new ApiKeyStatusDisplayNames(IdentityTestContext.DisplayNameResources()));
    }

    /// <summary>Builds a create command, letting a test replace one input.</summary>
    private CreateApiKeyCommand Command(
        string[]? scopes = null,
        string[]? cidrs = null,
        DateTimeOffset? expiresAt = null)
    {
        return new CreateApiKeyCommand(
            "Deploy pipeline",
            scopes ?? [ApiKeyScopes.ServerRead],
            cidrs,
            expiresAt,
            _administrator,
            "203.0.113.7",
            "agent");
    }

    /// <summary>Creating a key returns a well-formed plaintext once, with the prefix the row stores.</summary>
    [Fact]
    public async Task Creating_a_key_returns_a_well_formed_plaintext_with_the_prefix_the_row_stores()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(ApiKeyHasher.TryParse(result.Value.Secret.Reveal(), out var prefix));
        Assert.Equal(prefix, result.Value.Key.Prefix);
        Assert.Equal(ApiKeyStatus.Active, result.Value.Key.Status);
        Assert.Equal([ApiKeyScopes.ServerRead], result.Value.Key.Scopes);
        Assert.Equal("Deploy pipeline", result.Value.Key.Name);
    }

    /// <summary>The row keeps only the digest of the plaintext, never the plaintext itself.</summary>
    [Fact]
    public async Task The_stored_row_keeps_only_the_digest_of_the_plaintext()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var stored = Assert.Single(_context.ApiKeys);
        Assert.NotEqual(result.Value.Secret.Reveal(), stored.TokenHash);
        Assert.Equal(ApiKeyHasher.Digest(result.Value.Secret.Reveal()), stored.TokenHash);
        Assert.DoesNotContain(result.Value.Secret.Reveal(), stored.Name + stored.Prefix + stored.TokenHash + string.Join(',', stored.Scopes));
        Assert.Equal(_administrator, stored.CreatedByUserId);
        Assert.Equal(Now, stored.CreatedAt);
    }

    /// <summary>Two keys created in a row carry different plaintexts and different prefixes.</summary>
    [Fact]
    public async Task Two_keys_created_in_a_row_carry_different_plaintexts_and_prefixes()
    {
        var first = await NewHandler().HandleAsync(Command(), CancellationToken.None);
        var second = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.NotEqual(first.Value.Secret.Reveal(), second.Value.Secret.Reveal());
        Assert.NotEqual(first.Value.Key.Prefix, second.Value.Key.Prefix);
    }

    /// <summary>The audit entry names the prefix, name and scopes and never the secret or its digest.</summary>
    [Fact]
    public async Task The_audit_entry_names_prefix_name_and_scopes_and_never_the_secret_or_its_digest()
    {
        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ApiKeyCreated, entry.Action);
        Assert.Equal(_administrator, entry.ActorUserId);
        Assert.True(entry.Succeeded);
        Assert.Contains(result.Value.Key.Prefix, entry.Subject, StringComparison.Ordinal);
        Assert.Contains("Deploy pipeline", entry.Subject, StringComparison.Ordinal);
        Assert.Contains(ApiKeyScopes.ServerRead, entry.Subject, StringComparison.Ordinal);

        var everything = string.Join('|', entry.ActorUsername, entry.Subject, entry.IpAddress, entry.UserAgent);
        Assert.DoesNotContain(result.Value.Secret.Reveal(), everything);
        Assert.DoesNotContain(ApiKeyHasher.Digest(result.Value.Secret.Reveal()), everything);
        Assert.DoesNotContain(result.Value.Secret.Reveal()[^43..], everything);
    }

    /// <summary>The limit counts only keys still usable, so revoked and expired keys do not block a new one.</summary>
    [Fact]
    public async Task The_limit_counts_only_usable_keys()
    {
        for (var i = 0; i < ApiKeyLimits.MaxKeys - 1; i++)
        {
            await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));
        }

        await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), revoked: true);
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-10), expiresAt: Now.AddDays(-1));

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>At the limit of usable keys a new key is refused as a conflict and nothing is stored or journalled.</summary>
    [Fact]
    public async Task At_the_limit_of_usable_keys_a_new_key_is_refused_and_nothing_is_stored()
    {
        for (var i = 0; i < ApiKeyLimits.MaxKeys; i++)
        {
            await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));
        }

        var result = await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ApiKeyLimitReached", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(ApiKeyLimits.MaxKeys, _context.ApiKeys.Count());
        Assert.Empty(_audit.Written);
    }

    /// <summary>An expiry that is now or already past is refused with a validation error and nothing is stored.</summary>
    /// <param name="offsetSeconds">The expiry, in seconds from now.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-86400)]
    public async Task An_expiry_that_is_not_in_the_future_is_refused(int offsetSeconds)
    {
        var result = await NewHandler().HandleAsync(
            Command(expiresAt: Now.AddSeconds(offsetSeconds)),
            CancellationToken.None);

        Assert.Equal("ApiKeyExpiryInvalid", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(_context.ApiKeys);
        Assert.Empty(_audit.Written);
    }

    /// <summary>An expiry in the future is stored on the key.</summary>
    [Fact]
    public async Task An_expiry_in_the_future_is_stored_on_the_key()
    {
        var result = await NewHandler().HandleAsync(Command(expiresAt: Now.AddSeconds(1)), CancellationToken.None);

        Assert.Equal(Now.AddSeconds(1), result.Value.Key.ExpiresAt);
    }

    /// <summary>Repeated scopes and repeated address ranges are stored once, and a bare address becomes a single-address range.</summary>
    [Fact]
    public async Task Repeated_scopes_and_ranges_are_stored_once_and_a_bare_address_becomes_a_single_address_range()
    {
        var result = await NewHandler().HandleAsync(
            Command(
                scopes: [ApiKeyScopes.ServerRead, ApiKeyScopes.ServerRead],
                cidrs: ["203.0.113.0/24", "203.0.113.0/24", "198.51.100.9"]),
            CancellationToken.None);

        Assert.Equal([ApiKeyScopes.ServerRead], result.Value.Key.Scopes);
        Assert.Equal(["203.0.113.0/24", "198.51.100.9/32"], result.Value.Key.AllowedCidrs);
    }

    /// <summary>A key created with no range list (null) is not bound to any address.</summary>
    [Fact]
    public async Task A_key_created_with_no_range_list_is_not_bound()
    {
        await NewHandler().HandleAsync(Command(cidrs: null), CancellationToken.None);

        Assert.Null(Assert.Single(_context.ApiKeys).AllowedCidrs);
    }

    /// <summary>A stored key was created inside one lock-held transaction that was committed.</summary>
    [Fact]
    public async Task A_stored_key_is_created_inside_one_committed_lock_held_transaction()
    {
        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(1, _lock.Begins);
        Assert.Equal(1, _lock.Commits);
    }

    /// <summary>A key refused at the limit took the lock before counting and committed nothing.</summary>
    [Fact]
    public async Task A_key_refused_at_the_limit_took_the_lock_before_counting_and_committed_nothing()
    {
        for (var i = 0; i < ApiKeyLimits.MaxKeys; i++)
        {
            await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));
        }

        await NewHandler().HandleAsync(Command(), CancellationToken.None);

        Assert.Equal(1, _lock.Begins);
        Assert.Equal(0, _lock.Commits);
    }

    /// <summary>An expiry refused before any work does not take the lock at all.</summary>
    [Fact]
    public async Task A_refused_expiry_does_not_take_the_lock()
    {
        await NewHandler().HandleAsync(Command(expiresAt: Now), CancellationToken.None);

        Assert.Equal(0, _lock.Begins);
    }
}
