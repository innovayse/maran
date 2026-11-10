using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.Policies;
using Maran.Modules.Updates.Domain.ValueObjects;

namespace Maran.Modules.Updates.Tests.Domain.Policies;

/// <summary>When an update counts as in flight, and what the reaper does with a request whose report never arrived.</summary>
public sealed class PanelUpdateLedgerPolicyTests
{
    /// <summary>A marker younger than the grace period, or a fresh running record, means an update is in flight.</summary>
    [Fact]
    public void A_young_marker_or_a_fresh_running_record_is_an_update_in_flight()
    {
        Assert.True(PanelUpdateLedgerPolicy.IsInProgress(new PanelUpdateSignals(TimeSpan.FromMinutes(1), false)));
        Assert.True(PanelUpdateLedgerPolicy.IsInProgress(new PanelUpdateSignals(null, true)));
    }

    /// <summary>Neither signal, or only a marker past its grace period, is not an update in flight.</summary>
    [Fact]
    public void No_signal_or_a_stale_marker_is_not_an_update_in_flight()
    {
        Assert.False(PanelUpdateLedgerPolicy.IsInProgress(new PanelUpdateSignals(null, false)));
        Assert.False(PanelUpdateLedgerPolicy.IsInProgress(new PanelUpdateSignals(PanelUpdateLedgerPolicy.MarkerGrace, false)));
    }

    /// <summary>A check marker younger than the grace period is a check in flight (issue #516).</summary>
    [Fact]
    public void A_young_check_marker_is_a_check_in_flight()
    {
        Assert.True(PanelUpdateLedgerPolicy.IsCheckInProgress(TimeSpan.FromMinutes(1)));
    }

    /// <summary>No marker, or a check marker past its grace period, is not a check in flight: the button is offered again.</summary>
    [Fact]
    public void No_marker_or_a_stale_check_marker_is_not_a_check_in_flight()
    {
        Assert.False(PanelUpdateLedgerPolicy.IsCheckInProgress(null));
        Assert.False(PanelUpdateLedgerPolicy.IsCheckInProgress(PanelUpdateLedgerPolicy.MarkerGrace));
    }

    /// <summary>With no ledger there is nothing to reap, whatever the other signals say.</summary>
    [Fact]
    public void With_no_ledger_there_is_nothing_to_reap()
    {
        var verdict = PanelUpdateLedgerPolicy.Decide(new PanelUpdateSignals(TimeSpan.FromHours(1), false), null);

        Assert.Equal(PanelUpdateLedgerVerdict.Leave, verdict);
    }

    /// <summary>A run that root says is running is never reaped, however old the ledger.</summary>
    [Fact]
    public void A_run_that_is_running_is_never_reaped()
    {
        var verdict = PanelUpdateLedgerPolicy.Decide(new PanelUpdateSignals(null, true), TimeSpan.FromHours(3));

        Assert.Equal(PanelUpdateLedgerVerdict.Leave, verdict);
    }

    /// <summary>A marker waiting for root is left until it passes its grace period, and then the request counts as never started.</summary>
    [Fact]
    public void A_marker_root_never_picked_up_means_the_update_never_started()
    {
        var ledger = TimeSpan.FromMinutes(10);

        Assert.Equal(
            PanelUpdateLedgerVerdict.Leave,
            PanelUpdateLedgerPolicy.Decide(new PanelUpdateSignals(TimeSpan.FromMinutes(4), false), ledger));
        Assert.Equal(
            PanelUpdateLedgerVerdict.NeverStarted,
            PanelUpdateLedgerPolicy.Decide(new PanelUpdateSignals(PanelUpdateLedgerPolicy.MarkerGrace, false), ledger));
    }

    /// <summary>No marker, nothing running and an old ledger means the run ended without a report.</summary>
    [Fact]
    public void No_marker_nothing_running_and_an_old_ledger_means_the_outcome_is_unknown()
    {
        var quiet = new PanelUpdateSignals(null, false);

        Assert.Equal(PanelUpdateLedgerVerdict.Leave, PanelUpdateLedgerPolicy.Decide(quiet, TimeSpan.FromSeconds(30)));
        Assert.Equal(
            PanelUpdateLedgerVerdict.OutcomeUnknown,
            PanelUpdateLedgerPolicy.Decide(quiet, PanelUpdateLedgerPolicy.LedgerGrace + TimeSpan.FromSeconds(1)));
    }
}
