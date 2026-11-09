using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>Behavioural contract of <see cref="ApiKeyScopePolicy"/>.</summary>
public sealed class ApiKeyScopePolicyTests
{
    /// <summary>Issues a valid key holding the given scopes.</summary>
    private static ApiKey Key(params string[] scopes)
    {
        return ApiKey.Issue(
            Guid.NewGuid(),
            "CI",
            "0a1b2c3d",
            "digest",
            scopes,
            null,
            Guid.NewGuid(),
            new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero),
            null);
    }

    /// <summary>Overwrites the stored scopes the way a row written by another build would read.</summary>
    private static void StoreScopes(ApiKey key, params string[] scopes)
    {
        typeof(ApiKey).GetProperty(nameof(ApiKey.Scopes))!.SetValue(key, scopes);
    }

    /// <summary>The self scope is effective for every key, even one that stores none.</summary>
    [Fact]
    public void The_self_scope_is_effective_for_every_key()
    {
        var key = Key(ApiKeyScopes.ServerRead);
        StoreScopes(key);

        Assert.Equal([ApiKeyScopes.SelfRead], ApiKeyScopePolicy.EffectiveOf(key));
    }

    /// <summary>A stored scope of the vocabulary is effective alongside the self scope, in vocabulary order.</summary>
    [Fact]
    public void A_stored_scope_of_the_vocabulary_is_effective_beside_the_self_scope()
    {
        Assert.Equal([ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead], ApiKeyScopePolicy.EffectiveOf(Key(ApiKeyScopes.ServerRead)));
    }

    /// <summary>Stored scopes the running build does not know grant nothing, including near-misses in case.</summary>
    [Fact]
    public void Unknown_stored_scopes_grant_nothing()
    {
        var key = Key(ApiKeyScopes.SelfRead);
        StoreScopes(key, "backups.write", "SERVER.READ", "server.read ", "*", string.Empty);

        Assert.Equal([ApiKeyScopes.SelfRead], ApiKeyScopePolicy.EffectiveOf(key));
    }

    /// <summary>An unknown stored scope beside a known one leaves only the known one effective.</summary>
    [Fact]
    public void An_unknown_stored_scope_beside_a_known_one_leaves_only_the_known_one()
    {
        var key = Key(ApiKeyScopes.SelfRead);
        StoreScopes(key, "backups.write", ApiKeyScopes.ServerRead);

        Assert.Equal([ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead], ApiKeyScopePolicy.EffectiveOf(key));
    }

    /// <summary>A null key is refused rather than treated as one with no scopes.</summary>
    [Fact]
    public void A_null_key_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => { return ApiKeyScopePolicy.EffectiveOf(null!); });
    }

    /// <summary>A scope stored on a key whose owner may not hold it grants nothing, whichever way round the mismatch is.</summary>
    [Fact]
    public void A_stored_scope_the_owner_may_not_hold_grants_nothing()
    {
        var resellerKey = ApiKey.Issue(
            Guid.NewGuid(), "R", "0a1b2c3d", "digest", [ApiKeyScopes.AccountsRead], null, Guid.NewGuid(),
            new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), null, Guid.NewGuid());
        StoreScopes(resellerKey, ApiKeyScopes.ServerRead, ApiKeyScopes.AccountsRead);

        Assert.Equal([ApiKeyScopes.SelfRead, ApiKeyScopes.AccountsRead], ApiKeyScopePolicy.EffectiveOf(resellerKey));

        var administratorKey = Key(ApiKeyScopes.ServerRead);
        StoreScopes(administratorKey, ApiKeyScopes.ServerRead, ApiKeyScopes.AccountsRead, ApiKeyScopes.PlansRead);

        Assert.Equal([ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead], ApiKeyScopePolicy.EffectiveOf(administratorKey));
    }

    /// <summary>A write scope stored on an administrator-owned key grants nothing, while the same scope on a reseller's key is effective.</summary>
    [Fact]
    public void A_write_scope_is_effective_only_on_a_resellers_key()
    {
        var resellerKey = ApiKey.Issue(
            Guid.NewGuid(), "R", "0a1b2c3d", "digest", [ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete], null, Guid.NewGuid(),
            new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), null, Guid.NewGuid());

        Assert.Equal(
            [ApiKeyScopes.SelfRead, ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete],
            ApiKeyScopePolicy.EffectiveOf(resellerKey));

        var administratorKey = Key(ApiKeyScopes.ServerRead);
        StoreScopes(administratorKey, ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete);

        Assert.Equal([ApiKeyScopes.SelfRead], ApiKeyScopePolicy.EffectiveOf(administratorKey));
    }
}
