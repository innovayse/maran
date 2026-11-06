using Maran.Modules.Idempotency.Domain.Entities;
using Maran.Modules.Idempotency.Domain.Enums;
using Maran.Modules.Idempotency.Domain.Policies;
using Maran.Modules.Idempotency.Tests.TestSupport;

namespace Maran.Modules.Idempotency.Tests.Domain.Entities;

/// <summary>The states a record moves through and the limits it holds by construction.</summary>
public sealed class IdempotencyRecordTests
{
    /// <summary>The instant every record in these tests is created.</summary>
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A new record is a running reservation that lapses with its lease.</summary>
    [Fact]
    public void A_new_record_is_a_reservation_that_lapses_with_its_lease()
    {
        var record = New();

        Assert.Equal(IdempotencyState.InProgress, record.State);
        Assert.Null(record.StatusCode);
        Assert.Equal(Start + IdempotencyRetention.ReservationLease, record.ExpiresAt);
        Assert.False(record.IsExpired(Start));
        Assert.True(record.IsExpired(Start + IdempotencyRetention.ReservationLease));
    }

    /// <summary>Completing stores the response and moves expiry to the replay window counted from creation.</summary>
    [Fact]
    public void Completing_stores_the_response_and_extends_expiry_to_the_window()
    {
        var record = New();

        record.Complete(422, "application/json", "{\"code\":\"x\"}");

        Assert.Equal(IdempotencyState.Completed, record.State);
        Assert.Equal(422, record.StatusCode);
        Assert.Equal("application/json", record.ContentType);
        Assert.Equal("{\"code\":\"x\"}", record.ResponseBody);
        Assert.Equal(Start + IdempotencyRetention.CompletedWindow, record.ExpiresAt);
    }

    /// <summary>Only the token the record was reserved with owns it, and a completed record is owned by nobody.</summary>
    [Fact]
    public void Only_the_reserving_token_owns_a_running_record_and_a_completed_one_has_no_owner()
    {
        var token = Guid.NewGuid();
        var record = new IdempotencyRecord(
            Guid.NewGuid(), IdempotencyTestValues.AdministratorA, IdempotencyTestValues.Key,
            IdempotencyTestValues.HashOne, token, Start);

        Assert.True(record.IsReservedBy(token));
        Assert.False(record.IsReservedBy(Guid.NewGuid()));

        record.Complete(200, null, "{}");

        Assert.False(record.IsReservedBy(token));
        Assert.Null(record.ReservationToken);
    }

    /// <summary>A second completion does not rewrite the response a retry may already have replayed.</summary>
    [Fact]
    public void A_second_completion_keeps_the_first_response()
    {
        var record = New();
        record.Complete(200, null, "first");

        record.Complete(500, null, "second");

        Assert.Equal(200, record.StatusCode);
        Assert.Equal("first", record.ResponseBody);
    }

    /// <summary>A body exactly at the cap is kept; one byte over is dropped.</summary>
    [Fact]
    public void A_body_is_kept_at_the_cap_and_dropped_one_byte_over()
    {
        var atCap = New();
        var overCap = New();

        atCap.Complete(200, null, new string('a', IdempotencyRecord.ResponseBodyMaxBytes));
        overCap.Complete(200, null, new string('a', IdempotencyRecord.ResponseBodyMaxBytes + 1));

        Assert.NotNull(atCap.ResponseBody);
        Assert.Null(overCap.ResponseBody);
    }

    /// <summary>The cap counts bytes, so a short string of wide characters can exceed it.</summary>
    [Fact]
    public void The_cap_counts_bytes_not_characters()
    {
        var record = New();

        record.Complete(200, null, new string('ա', IdempotencyRecord.ResponseBodyMaxBytes / 2 + 1));

        Assert.Null(record.ResponseBody);
    }

    /// <summary>A record is created only from values the policy accepts.</summary>
    [Fact]
    public void A_record_cannot_be_created_from_a_value_the_policy_refuses()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return new IdempotencyRecord(
                Guid.NewGuid(), "bad caller",
                IdempotencyTestValues.Key, IdempotencyTestValues.HashOne, Guid.NewGuid(), Start);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return new IdempotencyRecord(
                Guid.NewGuid(),
                IdempotencyTestValues.AdministratorA, "bad key", IdempotencyTestValues.HashOne, Guid.NewGuid(), Start);
        });
        Assert.Throws<ArgumentException>(() =>
        {
            return new IdempotencyRecord(
                Guid.NewGuid(),
                IdempotencyTestValues.AdministratorA, IdempotencyTestValues.Key, "ABC", Guid.NewGuid(), Start);
        });
    }

    /// <summary>Creates a running reservation.</summary>
    /// <returns>The record.</returns>
    private static IdempotencyRecord New()
    {
        return new IdempotencyRecord(
            Guid.NewGuid(),
            IdempotencyTestValues.AdministratorA,
            IdempotencyTestValues.Key,
            IdempotencyTestValues.HashOne,
            Guid.NewGuid(),
            Start);
    }
}
