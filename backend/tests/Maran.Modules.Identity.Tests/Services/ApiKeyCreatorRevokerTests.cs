using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioural contract of <see cref="Maran.Modules.Identity.Services.ApiKeyCreatorRevoker"/>: it revokes the
/// keys the named logins created, and only those, and stages one entry for each revocation with its reason (issue #551).
/// </summary>
public sealed class ApiKeyCreatorRevokerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly string _database = Guid.NewGuid().ToString();
    private readonly IdentityDbContext _context;

    /// <summary>Opens the fixture's context over its own named in-memory database.</summary>
    public ApiKeyCreatorRevokerTests()
    {
        _context = IdentityTestContext.CreateOver(_database);
    }

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    /// Revoking changes the tracked rows and stages the entries, and neither reaches the database until the
    /// caller's one save: a fresh context sees both only after it.
    /// </summary>
    [Fact]
    public async Task Revoking_stages_the_keys_and_their_entries_and_commits_both_with_the_callers_save()
    {
        var creator = Guid.NewGuid();
        var (key, plaintext) = await ApiKeySeed.AddAsync(
            _context, Now.AddDays(-1), name: "Deploy", scopes: [ApiKeyScopes.ServerRead], createdBy: creator);
        var revoker = ErasureHandlers.KeyRevoker(_context);

        var revoked = await revoker.RevokeAsync(creator, CancellationToken.None);

        Assert.Equal(1, revoked);
        using (var beforeSave = IdentityTestContext.CreateOver(_database))
        {
            Assert.Null((await beforeSave.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == key.Id)).RevokedAt);
            Assert.Empty(await beforeSave.AuditEvents.AsNoTracking().ToListAsync());
        }

        await _context.SaveChangesAsync();
        using var afterSave = IdentityTestContext.CreateOver(_database);
        Assert.Equal(ErasureHandlers.Now, (await afterSave.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == key.Id)).RevokedAt);
        var entry = await afterSave.AuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal(IdentityAuditActions.ApiKeyRevoked, entry.Action);
        Assert.Equal($"{key.Prefix} Deploy [server.read] reason=CreatorDisabled", entry.Subject);
        Assert.Null(entry.ActorUserId);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(plaintext, string.Join('|', entry.ActorUsername, entry.Subject));
        Assert.DoesNotContain(key.TokenHash, entry.Subject);
    }

    /// <summary>A key another login created, and a key already revoked, are left as they were and get no entry.</summary>
    [Fact]
    public async Task Revoking_leaves_other_creators_keys_and_keeps_an_earlier_revocation_instant()
    {
        var creator = Guid.NewGuid();
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "mine", createdBy: creator);
        var (other, _) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "other");
        var (earlier, _) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-3), name: "earlier", createdBy: creator, revoked: true);
        var revoker = ErasureHandlers.KeyRevoker(_context);

        var revoked = await revoker.RevokeAsync(creator, CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.Equal(1, revoked);
        Assert.Null((await _context.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == other.Id)).RevokedAt);
        Assert.Equal(Now.AddDays(-3), (await _context.ApiKeys.AsNoTracking().SingleAsync(row => row.Id == earlier.Id)).RevokedAt);
        Assert.Contains("mine", (await _context.AuditEvents.AsNoTracking().SingleAsync()).Subject);
    }

    /// <summary>A login that created no key revokes nothing and stages nothing.</summary>
    [Fact]
    public async Task Revoking_for_a_login_with_no_keys_stages_nothing()
    {
        await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));

        var revoked = await ErasureHandlers.KeyRevoker(_context).RevokeAsync(Guid.NewGuid(), CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.Equal(0, revoked);
        Assert.Null((await _context.ApiKeys.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.Empty(await _context.AuditEvents.AsNoTracking().ToListAsync());
    }
}
