using Maran.Modules.Identity.Commands.SaveSecurityPolicy;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.SaveSecurityPolicy;

/// <summary>Behavioural contract of the security-policy save handler.</summary>
public sealed class SaveSecurityPolicyCommandHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    private readonly RecordingAuditWriter _audit = new();
    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    private static SaveSecurityPolicyCommand Command(int minimumPasswordLength = 16, bool force = true)
    {
        return new SaveSecurityPolicyCommand(minimumPasswordLength, force, 5, 30, "203.0.113.7", "agent");
    }

    private SaveSecurityPolicyCommandHandler NewHandler(SecurityPolicyCache cache)
    {
        var principal = new StubCurrentUser();

        return new SaveSecurityPolicyCommandHandler(
            _context,
            cache,
            new IdentityAuditJournal(_audit, principal),
            principal,
            _clock);
    }

    /// <summary>Saving twice leaves one row because the key is a constant.</summary>
    /// <remarks>
    /// Two rows would be two answers to every question the policy settles, and whichever was loaded
    /// first would be the one in force. The fixed primary key is what makes a second row impossible
    /// rather than merely unlikely.
    /// </remarks>
    [Fact]
    public async Task Saving_twice_leaves_one_row_because_the_key_is_a_constant()
    {
        var cache = TestSecurityPolicyCache.Over(_context);

        await NewHandler(cache).HandleAsync(Command(), CancellationToken.None);
        await NewHandler(cache).HandleAsync(Command(minimumPasswordLength: 18), CancellationToken.None);

        var row = Assert.Single(await _context.SecurityPolicies.ToListAsync());
        Assert.Equal(18, row.MinimumPasswordLength);
    }

    /// <summary>A saved policy is in force on the next read.</summary>
    [Fact]
    public async Task A_saved_policy_is_in_force_on_the_next_read()
    {
        var cache = TestSecurityPolicyCache.Over(_context);
        await cache.GetAsync(CancellationToken.None);

        await NewHandler(cache).HandleAsync(Command(minimumPasswordLength: 22), CancellationToken.None);

        Assert.Equal(22, (await cache.GetAsync(CancellationToken.None)).MinimumPasswordLength);
    }

    /// <summary>Saving the policy is journalled.</summary>
    [Fact]
    public async Task Saving_the_policy_is_journalled()
    {
        await NewHandler(TestSecurityPolicyCache.Over(_context)).HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.SecurityPolicySaved, entry.Action);
        Assert.True(entry.Succeeded);
    }
}
