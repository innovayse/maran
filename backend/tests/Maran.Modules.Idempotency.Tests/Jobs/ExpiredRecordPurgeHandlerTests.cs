using Maran.Modules.Idempotency.Domain.Entities;
using Maran.Modules.Idempotency.Domain.Policies;
using Maran.Modules.Idempotency.Jobs;
using Maran.Modules.Idempotency.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Idempotency.Tests.Jobs;

/// <summary>What the hourly purge removes and, as important, what it must leave.</summary>
public sealed class ExpiredRecordPurgeHandlerTests
{
    /// <summary>The clock the handler and the seeded rows are measured against.</summary>
    private readonly FixedClock _clock = new();

    /// <summary>The name of this test's private database.</summary>
    private readonly string _database = Guid.NewGuid().ToString();

    /// <summary>Reservations past their lease and responses past their window are removed.</summary>
    [Fact]
    public async Task Expired_reservations_and_expired_responses_are_removed()
    {
        var start = _clock.UtcNow;
        await SeedAsync("expired-reservation", start, completed: false);
        await SeedAsync("expired-response", start, completed: true);
        _clock.UtcNow = start + IdempotencyRetention.CompletedWindow + TimeSpan.FromMinutes(1);

        var purged = await Handler().HandleAsync(new ExpiredRecordPurgeRequested(), default);

        Assert.Equal(2, purged);
        Assert.Empty(await Remaining());
    }

    /// <summary>A running reservation inside its lease and a response inside its window both survive.</summary>
    [Fact]
    public async Task Records_inside_their_lease_or_window_are_kept()
    {
        var start = _clock.UtcNow;
        await SeedAsync("fresh-reservation", start, completed: false);
        await SeedAsync("fresh-response", start, completed: true);
        _clock.UtcNow = start + TimeSpan.FromMinutes(1);

        var purged = await Handler().HandleAsync(new ExpiredRecordPurgeRequested(), default);

        Assert.Equal(0, purged);
        Assert.Equal(2, (await Remaining()).Count);
    }

    /// <summary>A completed response outlives the five-minute lease: only its own 24-hour window removes it.</summary>
    [Fact]
    public async Task A_stored_response_outlives_the_reservation_lease()
    {
        var start = _clock.UtcNow;
        await SeedAsync("response", start, completed: true);
        _clock.UtcNow = start + IdempotencyRetention.ReservationLease + TimeSpan.FromMinutes(1);

        var purged = await Handler().HandleAsync(new ExpiredRecordPurgeRequested(), default);

        Assert.Equal(0, purged);
        Assert.Single(await Remaining());
    }

    /// <summary>A pass with more rows than one batch still removes them all.</summary>
    [Fact]
    public async Task A_pass_larger_than_one_batch_removes_every_expired_row()
    {
        var start = _clock.UtcNow;
        for (var index = 0; index < 1100; index++)
        {
            await SeedAsync("bulk-" + index, start, completed: false);
        }

        _clock.UtcNow = start + IdempotencyRetention.ReservationLease + TimeSpan.FromMinutes(1);

        var purged = await Handler().HandleAsync(new ExpiredRecordPurgeRequested(), default);

        Assert.Equal(1100, purged);
        Assert.Empty(await Remaining());
    }

    /// <summary>The handler under test, over a fresh context of this test's database.</summary>
    /// <returns>The handler.</returns>
    private ExpiredRecordPurgeHandler Handler()
    {
        return new ExpiredRecordPurgeHandler(
            InMemoryIdempotencyDatabase.Open(_database), _clock, NullLogger<ExpiredRecordPurgeHandler>.Instance);
    }

    /// <summary>Stores one record created at <paramref name="at"/>.</summary>
    /// <param name="key">The record's key.</param>
    /// <param name="at">When the record was created.</param>
    /// <param name="completed">True to store it as a finished response, false as a running reservation.</param>
    private async Task SeedAsync(string key, DateTimeOffset at, bool completed)
    {
        using var context = InMemoryIdempotencyDatabase.Open(_database);
        var record = new IdempotencyRecord(
            Guid.NewGuid(), IdempotencyTestValues.AdministratorA, key, IdempotencyTestValues.HashOne, Guid.NewGuid(), at);
        if (completed)
        {
            record.Complete(200, null, "{}");
        }

        context.IdempotencyRecords.Add(record);
        await context.SaveChangesAsync();
    }

    /// <summary>Reads every record still stored.</summary>
    /// <returns>The remaining records.</returns>
    private async Task<List<IdempotencyRecord>> Remaining()
    {
        using var context = InMemoryIdempotencyDatabase.Open(_database);
        return await context.IdempotencyRecords.ToListAsync();
    }
}
