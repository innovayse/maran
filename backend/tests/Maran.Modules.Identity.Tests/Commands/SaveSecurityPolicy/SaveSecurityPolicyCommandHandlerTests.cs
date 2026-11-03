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

    private static SaveSecurityPolicyCommand Command(
        int minimumPasswordLength = 16,
        bool force = true,
        bool forceForResellers = true)
    {
        return new SaveSecurityPolicyCommand(
            minimumPasswordLength,
            force,
            5,
            30,
            forceForResellers,
            "203.0.113.7",
            "agent");
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

    /// <summary>The reseller switch is stored as sent, on the first save and on a replacement.</summary>
    /// <remarks>
    /// The first save goes through the constructor and the second through <c>Replace</c>; both take
    /// the flag, and a handler that forgot it on either path would keep the default (on).
    /// </remarks>
    [Fact]
    public async Task The_reseller_switch_is_stored_as_sent_on_the_first_save_and_on_a_replacement()
    {
        var cache = TestSecurityPolicyCache.Over(_context);

        await NewHandler(cache).HandleAsync(Command(forceForResellers: false), CancellationToken.None);
        var created = Assert.Single(await _context.SecurityPolicies.ToListAsync());
        Assert.False(created.ForceTwoFactorForResellers);
        Assert.False((await cache.GetAsync(CancellationToken.None)).ForceTwoFactorForResellers);

        await NewHandler(cache).HandleAsync(Command(forceForResellers: true), CancellationToken.None);
        var replaced = Assert.Single(await _context.SecurityPolicies.ToListAsync());
        Assert.True(replaced.ForceTwoFactorForResellers);
        Assert.True((await cache.GetAsync(CancellationToken.None)).ForceTwoFactorForResellers);
    }

    /// <summary>A body that leaves the reseller switch out reads as on, never as off.</summary>
    /// <remarks>
    /// A client written before resellers existed saves the whole policy; reading its silence as off
    /// would turn off the protection of every reseller login.
    /// </remarks>
    [Fact]
    public async Task A_command_that_leaves_the_reseller_switch_out_means_on_not_off()
    {
        var command = new SaveSecurityPolicyCommand(16, false, 5, 30);

        Assert.True(command.ForceTwoFactorForResellers);

        await NewHandler(TestSecurityPolicyCache.Over(_context)).HandleAsync(command, CancellationToken.None);

        Assert.True(Assert.Single(await _context.SecurityPolicies.ToListAsync()).ForceTwoFactorForResellers);
    }

    /// <summary>Saving the policy is journalled.</summary>
    [Fact]
    public async Task Saving_the_policy_is_journalled()
    {
        await NewHandler(TestSecurityPolicyCache.Over(_context)).HandleAsync(Command(), CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(IdentityAuditActions.SecurityPolicySaved, entry.Action);
        Assert.True(entry.Succeeded);
    }
}
