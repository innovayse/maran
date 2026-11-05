using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>What <see cref="IdentityAuditJournal"/> records about API keys: their lifecycle and the calls made with them.</summary>
public sealed class IdentityAuditJournalApiKeyTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds the journal under test over the recording writer.</summary>
    private IdentityAuditJournal NewJournal()
    {
        return new IdentityAuditJournal(_audit, new StubCurrentUser());
    }

    /// <summary>A lifecycle entry names the key by prefix, name and scopes and by nothing that could be used as the key.</summary>
    [Fact]
    public async Task A_lifecycle_entry_names_prefix_name_and_scopes_and_nothing_that_could_be_the_key()
    {
        var (key, plaintext) = await ApiKeySeed.AddAsync(_context, Now, name: "Deploy", scopes: [ApiKeyScopes.ServerRead]);
        var administrator = Guid.NewGuid();

        await NewJournal().RecordApiKeyAsync(
            administrator,
            IdentityAuditActions.ApiKeyCreated,
            key,
            "203.0.113.7",
            "agent",
            CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ApiKeyCreated, entry.Action);
        Assert.Equal(administrator, entry.ActorUserId);
        Assert.Equal($"{key.Prefix} Deploy [server.read]", entry.Subject);
        Assert.True(entry.Succeeded);

        var everything = string.Join('|', entry.ActorUsername, entry.Subject, entry.IpAddress, entry.UserAgent);
        Assert.DoesNotContain(plaintext, everything);
        Assert.DoesNotContain(key.TokenHash, everything);
        Assert.DoesNotContain(ApiKeyHasher.Digest(plaintext), everything);
    }

    /// <summary>A null key is refused rather than journalled as a blank entry.</summary>
    [Fact]
    public async Task A_null_key_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await NewJournal().RecordApiKeyAsync(Guid.NewGuid(), IdentityAuditActions.ApiKeyRevoked, null!, "203.0.113.7", "agent", CancellationToken.None);
        });
    }

    /// <summary>A call entry is attributed to the key by its actor name, with no user id and no user agent.</summary>
    [Fact]
    public async Task A_call_entry_is_attributed_to_the_key_with_no_user_id_and_no_user_agent()
    {
        var caller = new ApiKeyCaller(Guid.NewGuid(), [ApiKeyScopes.SelfRead]);

        await NewJournal().RecordApiKeyCallAsync(
            caller,
            "GET",
            "api/v1/api-keys/self",
            200,
            "203.0.113.7",
            CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.ApiKeyCall, entry.Action);
        Assert.Equal(caller.ActorName, entry.ActorUsername);
        Assert.Null(entry.ActorUserId);
        Assert.Equal("GET api/v1/api-keys/self 200", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal(string.Empty, entry.UserAgent);
    }

    /// <summary>A call is recorded as failed from status 400 up and as succeeded below it.</summary>
    /// <param name="status">The status the call was answered with.</param>
    /// <param name="succeeded">Whether the entry says it succeeded.</param>
    [Theory]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(399, true)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(500, false)]
    public async Task A_call_is_recorded_as_failed_from_status_400_up(int status, bool succeeded)
    {
        await NewJournal().RecordApiKeyCallAsync(
            new ApiKeyCaller(Guid.NewGuid(), [ApiKeyScopes.SelfRead]),
            "GET",
            "api/v1/modules",
            status,
            "203.0.113.7",
            CancellationToken.None);

        Assert.Equal(succeeded, Assert.Single(_audit.Written).Succeeded);
    }

    /// <summary>A null caller is refused rather than journalled as an anonymous call.</summary>
    [Fact]
    public async Task A_null_caller_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await NewJournal().RecordApiKeyCallAsync(null!, "GET", "api/v1/modules", 200, "203.0.113.7", CancellationToken.None);
        });
    }
}
