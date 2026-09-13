using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// What the panel counts, when it decides an address is attacking it, and what it says when it does.
/// </summary>
public sealed class BruteForceDetectorTests : IDisposable
{
    private const string Attacker = "203.0.113.7";

    private const string Bystander = "198.51.100.4";

    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    private readonly RecordingMessageBus _bus = new();

    private readonly FakeClock _clock = new(Now);

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>One refused sign-in announces nothing.</summary>
    [Fact]
    public async Task One_refused_sign_in_announces_nothing()
    {
        var detector = NewDetector();

        await detector.RecordFailureAsync(Attacker, CancellationToken.None);

        Assert.Empty(_bus.Published);
    }

    /// <summary>The failure that reaches the threshold announces the address the count and the window.</summary>
    [Fact]
    public async Task The_failure_that_reaches_the_threshold_announces_the_address_the_count_and_the_window()
    {
        var detector = NewDetector();

        await FailAsync(detector, Attacker, BruteForceOptions().MaxFailuresPerAddress);

        var detection = Assert.IsType<BruteForceDetected>(Assert.Single(_bus.Published));
        Assert.Equal(Attacker, detection.IpAddress);
        Assert.Equal(BruteForceOptions().MaxFailuresPerAddress, detection.Failures);
        Assert.Equal(Now, detection.WindowStart);
    }

    /// <summary>One failure short of the threshold announces nothing.</summary>
    [Fact]
    public async Task One_failure_short_of_the_threshold_announces_nothing()
    {
        // The guard on every count test here: a detector that announced on any failure at all would
        // satisfy the test above and be wrong by twenty-four attempts.
        var detector = NewDetector();

        await FailAsync(detector, Attacker, BruteForceOptions().MaxFailuresPerAddress - 1);

        Assert.Empty(_bus.Published);
    }

    /// <summary>Two addresses each below the threshold are two counts and not one.</summary>
    [Fact]
    public async Task Two_addresses_each_below_the_threshold_are_two_counts_and_not_one()
    {
        var detector = NewDetector();
        var threshold = BruteForceOptions().MaxFailuresPerAddress;

        await FailAsync(detector, Attacker, threshold - 1);
        await FailAsync(detector, Bystander, threshold - 1);

        Assert.Empty(_bus.Published);
    }

    /// <summary>A window that has closed starts the count again from one.</summary>
    [Fact]
    public async Task A_window_that_has_closed_starts_the_count_again_from_one()
    {
        // Without this, twenty-five mistyped passwords spread over a year would eventually ban a
        // customer, and the word "window" in the policy would mean nothing.
        var options = BruteForceOptions();
        var detector = NewDetector();
        await FailAsync(detector, Attacker, options.MaxFailuresPerAddress - 1);

        _clock.Advance(options.Window + TimeSpan.FromSeconds(1));
        await detector.RecordFailureAsync(Attacker, CancellationToken.None);

        Assert.Empty(_bus.Published);
        Assert.Equal(1, (await _context.FailedLoginsByIp.SingleAsync()).Failures);
    }

    /// <summary>The count restarts once a detection is announced so the next failure is not a second attack.</summary>
    [Fact]
    public async Task The_count_restarts_once_a_detection_is_announced_so_the_next_failure_is_not_a_second_attack()
    {
        // The ban ladder on the other side escalates by EPISODE — fifteen minutes, an hour, a day.
        // A counter left standing at the threshold would announce again on the twenty-sixth failure
        // and the twenty-seventh, and three extra attempts would buy a twenty-four-hour ban that is
        // supposed to take three separate waves.
        var detector = NewDetector();
        await FailAsync(detector, Attacker, BruteForceOptions().MaxFailuresPerAddress);

        _clock.Advance(TimeSpan.FromSeconds(1));
        await detector.RecordFailureAsync(Attacker, CancellationToken.None);

        Assert.Single(_bus.Published);
        Assert.Equal(1, (await _context.FailedLoginsByIp.SingleAsync()).Failures);
    }

    /// <summary>A second detection needs a full count again and carries a window the first one cannot collide with.</summary>
    [Fact]
    public async Task A_second_detection_needs_a_full_count_again_and_carries_a_window_the_first_cannot_collide_with()
    {
        // The subscriber treats (address, window start) as the identity of a detection and refuses a
        // repeat as a redelivery. So a second wave that reused the first wave's window start would
        // be a ban that never happened — which is why the announced window is removed rather than
        // zeroed, and the next one begins at a real later failure.
        var detector = NewDetector();
        var threshold = BruteForceOptions().MaxFailuresPerAddress;
        await FailAsync(detector, Attacker, threshold);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await FailAsync(detector, Attacker, threshold);

        var detections = _bus.Published.Cast<BruteForceDetected>().ToList();
        Assert.Equal(2, detections.Count);
        Assert.Equal(Now, detections[0].WindowStart);
        Assert.Equal(Now + TimeSpan.FromMinutes(1), detections[1].WindowStart);
        Assert.NotEqual(detections[0].WindowStart, detections[1].WindowStart);
    }

    /// <summary>An announced window is removed rather than left standing at the threshold.</summary>
    [Fact]
    public async Task An_announced_window_is_removed_rather_than_left_standing_at_the_threshold()
    {
        var detector = NewDetector();

        await FailAsync(detector, Attacker, BruteForceOptions().MaxFailuresPerAddress);

        Assert.Empty(await _context.FailedLoginsByIp.ToListAsync());
    }

    /// <summary>A window that has closed is reclaimed so the table does not keep every address ever seen.</summary>
    [Fact]
    public async Task A_window_that_has_closed_is_reclaimed_so_the_table_does_not_keep_every_address_ever_seen()
    {
        // The answer to "is this a memory leak under sustained attack": a row lives for one window
        // after the address stops, and the next refused sign-in from anywhere sweeps it away.
        var options = BruteForceOptions();
        var detector = NewDetector();
        await detector.RecordFailureAsync(Bystander, CancellationToken.None);
        _clock.Advance(options.Window + TimeSpan.FromSeconds(1));

        await detector.RecordFailureAsync(Attacker, CancellationToken.None);

        var remaining = await _context.FailedLoginsByIp.Select(f => f.IpAddress).ToListAsync();
        Assert.Equal([Attacker], remaining);
    }

    /// <summary>A request whose connection reported no peer is not counted at all.</summary>
    [Fact]
    public async Task A_request_whose_connection_reported_no_peer_is_not_counted_at_all()
    {
        // Counting under the marker would pool every peer-less request into one bucket that names
        // nobody, and announcing that bucket would ask the firewall to ban a word.
        var detector = NewDetector();

        await FailAsync(detector, ClientAddress.Unknown, BruteForceOptions().MaxFailuresPerAddress);

        Assert.Empty(_bus.Published);
        Assert.Empty(await _context.FailedLoginsByIp.ToListAsync());
    }

    /// <summary>The threshold and the window are policy and not constants in the detector.</summary>
    [Fact]
    public async Task The_threshold_and_the_window_are_policy_and_not_constants_in_the_detector()
    {
        // An operator behind one office NAT raises the count; one who signs in alone lowers it. A
        // detector that ignored the setting would pass every other test in this file.
        var detector = NewDetector(new BruteForceOptions { MaxFailuresPerAddress = 3, WindowMinutes = 1 });

        await FailAsync(detector, Attacker, 3);

        var detection = Assert.IsType<BruteForceDetected>(Assert.Single(_bus.Published));
        Assert.Equal(3, detection.Failures);
    }

    /// <summary>An address whose window is still open keeps its row across a reclaiming sweep.</summary>
    [Fact]
    public async Task An_address_whose_window_is_still_open_keeps_its_row_across_a_reclaiming_sweep()
    {
        var detector = NewDetector();
        await detector.RecordFailureAsync(Bystander, CancellationToken.None);

        await detector.RecordFailureAsync(Attacker, CancellationToken.None);

        Assert.Equal(2, await _context.FailedLoginsByIp.CountAsync());
    }

    /// <summary>The count survives the object graph that produced it, because it is a row and not a field.</summary>
    [Fact]
    public async Task The_count_survives_the_object_graph_that_produced_it_because_it_is_a_row_and_not_a_field()
    {
        // A fresh detector on every call is what a real request gets: the service is scoped, so
        // nothing about it outlives the request. Only the row carries the count forward, which is
        // the same reason it survives a restart.
        var threshold = BruteForceOptions().MaxFailuresPerAddress;
        for (var attempt = 0; attempt < threshold; attempt++)
        {
            await NewDetector().RecordFailureAsync(Attacker, CancellationToken.None);
        }

        Assert.Single(_bus.Published);
    }

    /// <summary>The policy these tests measure against, so a changed default does not rewrite them.</summary>
    /// <returns>The shipped defaults.</returns>
    private static BruteForceOptions BruteForceOptions()
    {
        return new BruteForceOptions();
    }

    /// <summary>Refuses <paramref name="times"/> sign-ins from one address.</summary>
    /// <param name="detector">The detector under test.</param>
    /// <param name="address">The address they came from.</param>
    /// <param name="times">How many.</param>
    /// <returns>Resolves once every one of them has been counted.</returns>
    private static async Task FailAsync(BruteForceDetector detector, string address, int times)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            await detector.RecordFailureAsync(address, CancellationToken.None);
        }
    }

    /// <summary>Builds the detector over this test's context, bus and clock.</summary>
    /// <param name="options">The policy to run with; the shipped defaults when omitted.</param>
    /// <returns>The detector.</returns>
    private BruteForceDetector NewDetector(BruteForceOptions? options = null)
    {
        return new BruteForceDetector(
            _context,
            _bus,
            _clock,
            new OptionsWrapper<BruteForceOptions>(options ?? BruteForceOptions()),
            NullLogger<BruteForceDetector>.Instance);
    }
}
