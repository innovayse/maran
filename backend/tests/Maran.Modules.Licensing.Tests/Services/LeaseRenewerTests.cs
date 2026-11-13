using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Tests.Services;

/// <summary>
/// Covers <see cref="Maran.Modules.Licensing.Services.LeaseRenewer"/> over its real parts: the lease is fetched
/// and stored when, and only when, the answer is a verified, fresh, better lease for this licence and this server,
/// and no other answer ever replaces what is stored. Every refusal has its accepting control beside it, in the same
/// test or in the one before it, because a renewal mutated to refuse everything would pass every test that only
/// hands it a bad answer.
/// </summary>
public sealed class LeaseRenewerTests
{
    /// <summary>The host's machine id in the tests that need one.</summary>
    private const string ThisHost = "0123456789abcdef0123456789abcdef";

    /// <summary>The instant the harness starts at.</summary>
    private static readonly DateTimeOffset Start = RenewalHarness.Start;

    /// <summary>A verified fresh answer for this licence is stored exactly as received and read back as a standing lease.</summary>
    [Fact]
    public async Task A_verified_fresh_answer_for_this_licence_is_stored_and_the_gate_sees_it()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        var answer = RenewalHarness.Lease(Start);
        harness.Client.Returns(answer);

        var result = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.Renewed, result.Outcome);
        Assert.Equal(Start.AddDays(14), result.ValidUntil);
        Assert.Equal(answer, harness.StoredLease);
        var stored = Assert.IsType<LeaseReading.Present>(await harness.ReadStoredAsync());
        Assert.Equal("lic-001", stored.Lease.LicenceId);
        Assert.Equal(Start.AddDays(14), stored.Lease.ValidUntil);
        Assert.Equal(1, harness.Invalidator.Count);
    }

    /// <summary>The only thing asked about is the installed licence's id.</summary>
    [Fact]
    public async Task The_service_is_asked_about_the_installed_licences_id_and_nothing_else()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence(id: "lic-777"));
        harness.Client.Returns(RenewalHarness.Lease(Start, licence: "lic-777"));

        var result = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.Renewed, result.Outcome);
        Assert.Equal(["lic-777"], harness.Client.Requests);
    }

    /// <summary>An answer signed by another key is rejected and writes nothing; the right key is the control.</summary>
    [Fact]
    public async Task An_answer_signed_by_another_key_is_rejected_and_nothing_is_written()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Returns(LeaseEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex, "lic-001", Start, Start.AddDays(14)));

        var forged = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.AnswerRejected, forged.Outcome);
        Assert.Null(harness.StoredLease);
        Assert.Equal(0, harness.Invalidator.Count);

        harness.Client.Returns(RenewalHarness.Lease(Start));
        var genuine = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.Renewed, genuine.Outcome);
    }

    /// <summary>The licence key does not stand in for the lease key: keys are separate (decision D3).</summary>
    [Fact]
    public async Task An_answer_signed_with_the_licence_key_is_rejected()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Returns(LeaseEnvelopeBuilder.Build(TestKeys.RealPrivateKeyHex, "lic-001", Start, Start.AddDays(14)));

        var result = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.AnswerRejected, result.Outcome);
        Assert.Null(harness.StoredLease);
    }

    /// <summary>A bad answer never overwrites a good lease: the stored file is byte for byte what it was.</summary>
    [Fact]
    public async Task A_bad_answer_leaves_a_good_stored_lease_byte_for_byte()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        var good = RenewalHarness.Lease(Start.AddHours(-21));
        harness.StoreLease(good);

        foreach (var bad in new[]
        {
            LeaseEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex, "lic-001", Start, Start.AddDays(14)),
            RenewalHarness.Lease(Start, licence: "lic-999"),
            "not json at all",
            "{\"payload\":{},\"signature\":\"AAAA\"}",
            string.Empty,
        })
        {
            harness.Client.Returns(bad);

            var result = await harness.Renewer.RenewAsync(CancellationToken.None);

            Assert.Equal(LeaseRenewalOutcome.AnswerRejected, result.Outcome);
            Assert.Equal(good, harness.StoredLease);
        }

        Assert.Equal(0, harness.Invalidator.Count);

        // The accepting control: the same stored lease IS replaced by a good answer.
        var better = RenewalHarness.Lease(Start);
        harness.Client.Returns(better);

        Assert.Equal(LeaseRenewalOutcome.Renewed, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);
        Assert.Equal(better, harness.StoredLease);
    }

    /// <summary>A payload that is not of kind lease is rejected even when the lease key signed it.</summary>
    [Fact]
    public async Task A_correctly_signed_payload_of_another_kind_is_rejected()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Returns(
            LeaseEnvelopeBuilder.Build(TestKeys.LeasePrivateKeyHex, "lic-001", Start, Start.AddDays(14), kind: "licence"));

        var result = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.AnswerRejected, result.Outcome);
        Assert.Null(harness.StoredLease);
    }

    /// <summary>A lease for another licence is rejected; one for this licence is the control.</summary>
    [Fact]
    public async Task An_answer_for_another_licence_is_rejected()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Returns(RenewalHarness.Lease(Start, licence: "lic-999"));

        var other = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.AnswerRejected, other.Outcome);
        Assert.Null(harness.StoredLease);

        harness.Client.Returns(RenewalHarness.Lease(Start));

        Assert.Equal(LeaseRenewalOutcome.Renewed, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);
    }

    /// <summary>A lease bound to another machine is rejected; one bound to this machine is the control.</summary>
    [Fact]
    public async Task An_answer_bound_to_another_machine_is_rejected_and_one_bound_to_this_machine_is_stored()
    {
        using var harness = new RenewalHarness(ThisHost);
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Returns(RenewalHarness.Lease(Start, server: "fedcba9876543210fedcba9876543210"));

        var other = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.AnswerRejected, other.Outcome);
        Assert.Null(harness.StoredLease);

        harness.Client.Returns(RenewalHarness.Lease(Start, server: ThisHost));

        Assert.Equal(LeaseRenewalOutcome.Renewed, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);
    }

    /// <summary>An old lease replayed as a renewal is rejected; one issued within the day is the control.</summary>
    [Fact]
    public async Task An_answer_issued_more_than_a_day_ago_is_rejected_and_one_issued_within_the_day_is_stored()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Returns(RenewalHarness.Lease(Start.AddDays(-2)));

        var replayed = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.AnswerRejected, replayed.Outcome);
        Assert.Null(harness.StoredLease);

        harness.Client.Returns(RenewalHarness.Lease(Start.AddHours(-23)));

        Assert.Equal(LeaseRenewalOutcome.Renewed, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);
    }

    /// <summary>A lease issued in the future beyond the skew bound is rejected; one inside the bound is the control.</summary>
    [Fact]
    public async Task An_answer_dated_in_the_future_is_rejected_and_one_inside_the_skew_bound_is_stored()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Returns(RenewalHarness.Lease(Start.AddHours(1)));

        var future = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.AnswerRejected, future.Outcome);
        Assert.Null(harness.StoredLease);

        harness.Client.Returns(RenewalHarness.Lease(Start.AddMinutes(1)));

        Assert.Equal(LeaseRenewalOutcome.Renewed, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);
    }

    /// <summary>An answer that already ended is rejected whatever it was issued as.</summary>
    [Fact]
    public async Task An_answer_that_has_already_ended_is_rejected()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Returns(RenewalHarness.Lease(Start.AddHours(-2), validUntil: Start.AddHours(-1)));

        var result = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.AnswerRejected, result.Outcome);
        Assert.Null(harness.StoredLease);
    }

    /// <summary>A genuine lease that would shorten the standing cover is not written; one that extends it is the control.</summary>
    [Fact]
    public async Task An_answer_that_does_not_extend_the_standing_lease_is_not_written()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        var standing = RenewalHarness.Lease(Start.AddHours(-21));
        harness.StoreLease(standing);
        harness.Client.Returns(RenewalHarness.Lease(Start, validUntil: Start.AddDays(10)));

        var shorter = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.NotNewer, shorter.Outcome);
        Assert.Equal(standing, harness.StoredLease);
        Assert.Equal(0, harness.Invalidator.Count);

        var longer = RenewalHarness.Lease(Start);
        harness.Client.Returns(longer);

        Assert.Equal(LeaseRenewalOutcome.Renewed, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);
        Assert.Equal(longer, harness.StoredLease);
    }

    /// <summary>Answers that carry no usable body map to their own outcomes and write nothing.</summary>
    [Theory]
    [InlineData(LeaseServiceAnswerKind.Unreachable, LeaseRenewalOutcome.Unreachable)]
    [InlineData(LeaseServiceAnswerKind.Refused, LeaseRenewalOutcome.Refused)]
    [InlineData(LeaseServiceAnswerKind.NotConfigured, LeaseRenewalOutcome.NotConfigured)]
    [InlineData(LeaseServiceAnswerKind.TooLarge, LeaseRenewalOutcome.AnswerRejected)]
    public async Task An_answer_without_a_body_maps_to_its_outcome_and_writes_nothing(
        LeaseServiceAnswerKind kind,
        LeaseRenewalOutcome expected)
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Answer = LeaseServiceAnswer.Without(kind);

        var result = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
        Assert.Null(harness.StoredLease);
        Assert.Equal(0, harness.Invalidator.Count);
        Assert.Single(harness.Client.Requests);
    }

    /// <summary>An offline licence is never renewed over the network; the same licence online asks once.</summary>
    [Fact]
    public async Task An_offline_licence_asks_nothing_and_an_online_one_asks_once()
    {
        using var offline = new RenewalHarness();
        offline.InstallLicence(RenewalHarness.Licence(offline: true));
        offline.Client.Returns(RenewalHarness.Lease(Start));

        var skipped = await offline.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.SkippedOffline, skipped.Outcome);
        Assert.Empty(offline.Client.Requests);
        Assert.Null(offline.StoredLease);

        using var online = new RenewalHarness();
        online.InstallLicence(RenewalHarness.Licence());
        online.Client.Returns(RenewalHarness.Lease(Start));

        await online.Renewer.RenewAsync(CancellationToken.None);

        Assert.Single(online.Client.Requests);
    }

    /// <summary>With no licence, a licence the verifier refuses, or one that has expired, nothing is asked.</summary>
    [Fact]
    public async Task Without_a_valid_licence_nothing_is_asked()
    {
        using var harness = new RenewalHarness();
        harness.Client.Returns(RenewalHarness.Lease(Start));

        Assert.Equal(LeaseRenewalOutcome.SkippedNoLicence, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);

        harness.InstallLicence(LicenceEnvelopeBuilder.Build(TestKeys.OtherPrivateKeyHex, id: "lic-001", expiry: Start.AddDays(90)));

        Assert.Equal(LeaseRenewalOutcome.SkippedNoLicence, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);

        harness.InstallLicence(RenewalHarness.Licence());
        harness.Clock.Advance(TimeSpan.FromDays(100));

        Assert.Equal(LeaseRenewalOutcome.SkippedNoLicence, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);
        Assert.Empty(harness.Client.Requests);

        harness.Clock.Advance(TimeSpan.FromDays(-100));
        harness.Client.Returns(RenewalHarness.Lease(harness.Clock.UtcNow));

        Assert.Equal(LeaseRenewalOutcome.Renewed, (await harness.Renewer.RenewAsync(CancellationToken.None)).Outcome);
        Assert.Single(harness.Client.Requests);
    }

    /// <summary>A lease issued an hour ago is not renewed and nothing is asked; one issued 21 hours ago is.</summary>
    [Fact]
    public async Task A_recent_lease_is_not_renewed_and_a_21_hour_old_one_is()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.StoreLease(RenewalHarness.Lease(Start.AddHours(-1)));
        harness.Client.Returns(RenewalHarness.Lease(Start));

        var recent = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.NotDue, recent.Outcome);
        Assert.Empty(harness.Client.Requests);

        harness.StoreLease(RenewalHarness.Lease(Start.AddHours(-21)));

        var old = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.Renewed, old.Outcome);
        Assert.Single(harness.Client.Requests);
    }

    /// <summary>A lease issued for a licence that was replaced while the service was being asked is dropped unwritten.</summary>
    [Fact]
    public async Task An_answer_for_a_licence_replaced_in_the_meantime_is_dropped()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        harness.Client.Returns(RenewalHarness.Lease(Start));
        harness.Client.WhileAsking = () => { harness.InstallLicence(RenewalHarness.Licence(id: "lic-002")); };

        var result = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.LicenceChanged, result.Outcome);
        Assert.Null(harness.StoredLease);
        Assert.Equal(0, harness.Invalidator.Count);
    }

    /// <summary>A write that fails is an outcome, not an exception, and leaves the stored lease alone.</summary>
    [Fact]
    public async Task A_failed_write_is_reported_and_the_stored_lease_is_untouched()
    {
        using var harness = new RenewalHarness(writer: new ThrowingLeaseWriter());
        harness.InstallLicence(RenewalHarness.Licence());
        var standing = RenewalHarness.Lease(Start.AddHours(-21));
        harness.StoreLease(standing);
        harness.Client.Returns(RenewalHarness.Lease(Start));

        var result = await harness.Renewer.RenewAsync(CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.WriteFailed, result.Outcome);
        Assert.Null(result.ValidUntil);
        Assert.Equal(standing, harness.StoredLease);
        Assert.Equal(0, harness.Invalidator.Count);
    }

    /// <summary>A cancelled pass propagates the cancellation and is not mistaken for a failed one.</summary>
    [Fact]
    public async Task A_cancelled_pass_is_cancelled_and_not_an_outcome()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => { return harness.Renewer.RenewAsync(cancelled.Token); });
    }

    /// <summary>A pass named for a licence that is not the installed one asks the service nothing; the named licence is the control.</summary>
    [Fact]
    public async Task A_pass_named_for_another_licence_asks_nothing_and_the_installed_licence_is_renewed()
    {
        using var harness = new RenewalHarness();
        harness.InstallLicence(RenewalHarness.Licence("lic-002"));
        harness.Client.Returns(RenewalHarness.Lease(Start, licence: "lic-002"));

        var other = await harness.Renewer.RenewAsync("lic-001", CancellationToken.None);

        Assert.Equal(LeaseRenewalOutcome.LicenceChanged, other.Outcome);
        Assert.Empty(harness.Client.Requests);
        Assert.Equal(
            LeaseRenewalOutcome.Renewed,
            (await harness.Renewer.RenewAsync("lic-002", CancellationToken.None)).Outcome);
    }
}
