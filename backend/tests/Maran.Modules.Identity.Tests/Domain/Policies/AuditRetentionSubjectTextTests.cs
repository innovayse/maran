using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>Behavioural contract of the retention entries' subject text.</summary>
public sealed class AuditRetentionSubjectTextTests
{
    /// <summary>A cutoff in a non-UTC offset, which the text must restate in UTC.</summary>
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 8, 12, 0, 0, TimeSpan.FromHours(4));

    /// <summary>"Keep indefinitely" is one fixed word, on either side of a change.</summary>
    [Fact]
    public void A_change_spells_no_limit_as_the_fixed_word()
    {
        Assert.Equal("from indefinite to 90", AuditRetentionSubjectText.Change(null, 90));
        Assert.Equal("from 90 to indefinite", AuditRetentionSubjectText.Change(90, null));
    }

    /// <summary>A run states the window, the cutoff in UTC, the count and the chain index.</summary>
    [Fact]
    public void A_run_states_the_window_the_utc_cutoff_the_count_and_the_chain_index()
    {
        Assert.Equal(
            "retentionDays=30 cutoff=2026-10-08T08:00:00.0000000Z purged=12 throughChainIndex=345",
            AuditRetentionSubjectText.Run(30, Cutoff, 12, 345));
    }

    /// <summary>A run with retention off still states itself, with no cutoff.</summary>
    [Fact]
    public void A_run_with_retention_off_states_that_there_was_no_cutoff()
    {
        Assert.Equal(
            "retentionDays=indefinite cutoff=none purged=0 throughChainIndex=0",
            AuditRetentionSubjectText.Run(null, null, 0, 0));
    }

    /// <summary>A failed run is spelled apart from a finished one.</summary>
    [Fact]
    public void A_failed_run_is_spelled_apart_from_a_finished_one()
    {
        Assert.Equal("retentionDays=30 failed", AuditRetentionSubjectText.Failed(30));
    }
}
