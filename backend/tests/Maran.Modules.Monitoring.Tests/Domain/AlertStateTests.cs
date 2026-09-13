using Maran.Modules.Monitoring.Domain.Entities;
using Maran.Modules.Monitoring.Domain.Enums;

namespace Maran.Modules.Monitoring.Tests.Domain;

/// <summary>
/// The state machine that turns a condition true every minute into exactly one mail.
/// </summary>
public sealed class AlertStateTests
{
    /// <summary>The instant every fixture's first observation is made.</summary>
    private static readonly DateTimeOffset Start = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Ten consecutive breaches raise once; the next fifty raise nothing.</summary>
    /// <remarks>
    /// This is the deduplication guarantee stated as a test. A threshold check would return "in
    /// alarm" on every one of these sixty observations, which is sixty mails about one full disk.
    /// </remarks>
    [Fact]
    public void Ten_consecutive_breaching_samples_raise_the_alert_exactly_once()
    {
        var state = new AlertState(Guid.NewGuid(), AlertKind.DiskUsage, "/", Start);

        var transitions = new List<AlertTransition>();
        for (var observation = 0; observation < 60; observation++)
        {
            transitions.Add(state.Observe(breaching: true, Start.AddMinutes(observation)));
        }

        Assert.Single(transitions, transition =>
        {
            return transition == AlertTransition.Raised;
        });
        Assert.Equal(AlertTransition.Raised, transitions[AlertState.BreachesBeforeAlert - 1]);
        Assert.True(state.IsFiring);
        Assert.Equal(Start.AddMinutes(AlertState.BreachesBeforeAlert - 1), state.RaisedAt);
    }

    /// <summary>Nine consecutive breaches raise nothing: the tenth is the threshold, not the ninth.</summary>
    [Fact]
    public void Nine_consecutive_breaches_raise_nothing()
    {
        var state = new AlertState(Guid.NewGuid(), AlertKind.DiskUsage, "/", Start);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert - 1; observation++)
        {
            Assert.Equal(AlertTransition.None, state.Observe(breaching: true, Start.AddMinutes(observation)));
        }

        Assert.False(state.IsFiring);
        Assert.Null(state.RaisedAt);
    }

    /// <summary>A healthy observation resets the counter, so scattered breaches never raise.</summary>
    [Fact]
    public void A_healthy_sample_resets_the_counter_so_scattered_breaches_never_raise()
    {
        var state = new AlertState(Guid.NewGuid(), AlertKind.DiskUsage, "/", Start);

        for (var observation = 0; observation < 60; observation++)
        {
            var breaching = observation % 2 == 0;
            Assert.Equal(AlertTransition.None, state.Observe(breaching, Start.AddMinutes(observation)));
        }

        Assert.False(state.IsFiring);
    }

    /// <summary>Returning to health closes the open episode once, and staying healthy closes nothing further.</summary>
    [Fact]
    public void Returning_to_health_resolves_the_open_episode_exactly_once()
    {
        var state = new AlertState(Guid.NewGuid(), AlertKind.ServiceStopped, "WebServer", Start);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            state.Observe(breaching: true, Start.AddMinutes(observation));
        }

        Assert.Equal(AlertTransition.Resolved, state.Observe(breaching: false, Start.AddMinutes(10)));
        Assert.Equal(AlertTransition.None, state.Observe(breaching: false, Start.AddMinutes(11)));
        Assert.False(state.IsFiring);
        Assert.Null(state.RaisedAt);
    }

    /// <summary>A condition that never breached resolves nothing when it is observed healthy.</summary>
    /// <remarks>
    /// Otherwise every healthy sample on every panel would announce that something had recovered.
    /// </remarks>
    [Fact]
    public void A_condition_that_never_fired_resolves_nothing()
    {
        var state = new AlertState(Guid.NewGuid(), AlertKind.DiskUsage, "/", Start);

        Assert.Equal(AlertTransition.None, state.Observe(breaching: false, Start.AddMinutes(1)));
    }

    /// <summary>A second episode raises again, because the first one was closed.</summary>
    [Fact]
    public void A_condition_that_recovers_and_breaches_again_raises_a_second_time()
    {
        var state = new AlertState(Guid.NewGuid(), AlertKind.DiskUsage, "/", Start);

        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            state.Observe(breaching: true, Start.AddMinutes(observation));
        }

        state.Observe(breaching: false, Start.AddMinutes(20));

        var transitions = new List<AlertTransition>();
        for (var observation = 0; observation < AlertState.BreachesBeforeAlert; observation++)
        {
            transitions.Add(state.Observe(breaching: true, Start.AddMinutes(30 + observation)));
        }

        Assert.Equal(AlertTransition.Raised, transitions[^1]);
    }
}
