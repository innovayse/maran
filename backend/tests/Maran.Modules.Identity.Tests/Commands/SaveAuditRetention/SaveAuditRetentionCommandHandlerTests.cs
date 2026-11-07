using Maran.Modules.Identity.Commands.SaveAuditRetention;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.SaveAuditRetention;

/// <summary>Behavioural contract of the audit-retention save handler.</summary>
public sealed class SaveAuditRetentionCommandHandlerTests : IDisposable
{
    /// <summary>The instant the clock reports.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The in-memory context the handler writes to.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();
    /// <summary>Keeps the entries the journal writes.</summary>
    private readonly RecordingAuditWriter _audit = new();
    /// <summary>The clock that stamps the row.</summary>
    private readonly FakeClock _clock = new(Now);
    /// <summary>The signed-in administrator.</summary>
    private readonly StubCurrentUser _principal = new();

    /// <summary>Releases the in-memory context this test owns.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Builds a command with the server-stamped members filled in.</summary>
    /// <param name="days">The window in days, or null for keep indefinitely.</param>
    /// <returns>The command.</returns>
    private static SaveAuditRetentionCommand Command(int? days)
    {
        return new SaveAuditRetentionCommand(days, "203.0.113.7", "agent");
    }

    /// <summary>Builds the handler over this test's context, journal and clock.</summary>
    /// <returns>The handler under test.</returns>
    private SaveAuditRetentionCommandHandler NewHandler()
    {
        return new SaveAuditRetentionCommandHandler(
            _context,
            new IdentityAuditJournal(_audit, _principal),
            _principal,
            _clock);
    }

    /// <summary>The first save creates the row with the window as sent.</summary>
    [Fact]
    public async Task The_first_save_creates_the_row_with_the_window_as_sent()
    {
        var result = await NewHandler().HandleAsync(Command(90), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(await _context.AuditRetentionPolicies.ToListAsync());
        Assert.Equal(90, row.RetentionDays);
        Assert.Equal(Now, row.UpdatedAt);
    }

    /// <summary>Saving twice leaves one row because the key is a constant.</summary>
    [Fact]
    public async Task Saving_twice_leaves_one_row_because_the_key_is_a_constant()
    {
        await NewHandler().HandleAsync(Command(90), CancellationToken.None);
        await NewHandler().HandleAsync(Command(365), CancellationToken.None);

        var row = Assert.Single(await _context.AuditRetentionPolicies.ToListAsync());
        Assert.Equal(AuditRetentionPolicy.SingletonId, row.Id);
        Assert.Equal(365, row.RetentionDays);
    }

    /// <summary>Saving "keep indefinitely" stores null, not zero and not a large number.</summary>
    [Fact]
    public async Task Saving_keep_indefinitely_stores_null()
    {
        await NewHandler().HandleAsync(Command(90), CancellationToken.None);

        await NewHandler().HandleAsync(Command(null), CancellationToken.None);

        Assert.Null(Assert.Single(await _context.AuditRetentionPolicies.ToListAsync()).RetentionDays);
    }

    /// <summary>The change is journalled by the signed-in administrator with the old and the new window.</summary>
    [Fact]
    public async Task The_change_is_journalled_with_the_old_and_the_new_window()
    {
        await NewHandler().HandleAsync(Command(90), CancellationToken.None);
        await NewHandler().HandleAsync(Command(null), CancellationToken.None);

        Assert.Equal(2, _audit.Written.Count);
        Assert.All(_audit.Written, entry =>
        {
            Assert.Equal(IdentityAuditActions.AuditRetentionChanged, entry.Action);
            Assert.True(entry.Succeeded);
            Assert.Equal(_principal.UserId, entry.ActorUserId);
            Assert.Equal("203.0.113.7", entry.IpAddress);
        });
        Assert.Equal("from indefinite to 90", _audit.Written[0].Subject);
        Assert.Equal("from 90 to indefinite", _audit.Written[1].Subject);
    }

    /// <summary>
    /// A value below the floor that reaches the handler without the validator, as a command dispatched
    /// on the bus does, is refused by the entity and nothing is stored or journalled.
    /// </summary>
    [Fact]
    public async Task A_window_below_the_floor_is_refused_by_the_entity_and_nothing_is_journalled()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () =>
            {
                return NewHandler().HandleAsync(Command(AuditRetentionPolicy.MinimumDays - 1), CancellationToken.None);
            });

        Assert.Empty(await _context.AuditRetentionPolicies.ToListAsync());
        Assert.Empty(_audit.Written);
    }

    /// <summary>The entity refuses a replacement above the ceiling too, not only the first value.</summary>
    [Fact]
    public async Task A_replacement_above_the_ceiling_is_refused_and_the_stored_window_stays()
    {
        await NewHandler().HandleAsync(Command(90), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () =>
            {
                return NewHandler().HandleAsync(Command(AuditRetentionPolicy.MaximumDays + 1), CancellationToken.None);
            });

        Assert.Equal(90, Assert.Single(await _context.AuditRetentionPolicies.ToListAsync()).RetentionDays);
        Assert.Single(_audit.Written);
    }
}
