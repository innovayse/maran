using Maran.Modules.Identity.Commands.RevokeApiKey;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.RevokeApiKey;

/// <summary>Behavioural contract of <see cref="RevokeApiKeyCommandHandler"/>.</summary>
public sealed class RevokeApiKeyCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly FakeClock _clock = new(Now);
    private readonly RecordingAuditWriter _audit = new();
    private readonly Guid _administrator = Guid.NewGuid();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds the handler under test over the fixture's context, clock and journal.</summary>
    private RevokeApiKeyCommandHandler NewHandler()
    {
        return new RevokeApiKeyCommandHandler(_context, _clock, new IdentityAuditJournal(_audit, new StubCurrentUser()));
    }

    /// <summary>Builds a revoke command for a key, from the fixture's administrator.</summary>
    private RevokeApiKeyCommand Command(Guid keyId)
    {
        return new RevokeApiKeyCommand(keyId, _administrator, "203.0.113.7", "agent");
    }

    /// <summary>Revoking an active key stamps the revocation time and writes one audit entry for the administrator.</summary>
    [Fact]
    public async Task Revoking_an_active_key_stamps_the_time_and_writes_one_audit_entry()
    {
        var (key, plaintext) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));

        var result = await NewHandler().HandleAsync(Command(key.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, (await _context.ApiKeys.SingleAsync()).RevokedAt);
        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ApiKeyRevoked, entry.Action);
        Assert.Equal(_administrator, entry.ActorUserId);
        Assert.Contains(key.Prefix, entry.Subject);
        Assert.DoesNotContain(plaintext, entry.Subject);
    }

    /// <summary>Revoking an already revoked key succeeds, keeps the first revocation time and writes no second entry.</summary>
    [Fact]
    public async Task Revoking_a_revoked_key_succeeds_keeps_the_first_time_and_writes_no_second_entry()
    {
        var (key, _) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1));
        await NewHandler().HandleAsync(Command(key.Id), CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(1));

        var again = await NewHandler().HandleAsync(Command(key.Id), CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.Equal(Now, (await _context.ApiKeys.SingleAsync()).RevokedAt);
        Assert.Single(_audit.Written);
    }

    /// <summary>Revoking a key that does not exist is a not-found error and writes nothing.</summary>
    [Fact]
    public async Task Revoking_an_unknown_key_is_not_found_and_writes_nothing()
    {
        var result = await NewHandler().HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ApiKeyNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_audit.Written);
    }

    /// <summary>Revoking one key leaves every other key untouched.</summary>
    [Fact]
    public async Task Revoking_one_key_leaves_the_others_untouched()
    {
        var (target, _) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "target");
        var (other, _) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), name: "other");

        await NewHandler().HandleAsync(Command(target.Id), CancellationToken.None);

        Assert.Null((await _context.ApiKeys.SingleAsync(key => key.Id == other.Id)).RevokedAt);
    }
}
