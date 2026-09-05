using Maran.Modules.Monitoring.Services;
using Maran.Modules.Monitoring.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring.Tests.Services;

/// <summary>
/// How this module records work nobody asked for: one shared spelling of the system actor, and no
/// invented request origin.
/// </summary>
public sealed class MonitoringAuditJournalTests
{
    /// <summary>An unattended entry names the panel through the one shared definition.</summary>
    [Fact]
    public async Task An_unattended_entry_names_the_panel_through_the_one_shared_definition()
    {
        var audit = new RecordingAuditWriter();
        var journal = new MonitoringAuditJournal(audit, new FakeCurrentUser());

        await journal.RecordSystemAsync(AuditActions.AlertRaised, "DiskFull:/", succeeded: true, CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(SystemAuditEntry.NameFor(MonitoringAuditJournal.ModuleName), entry.ActorUsername);
    }

    /// <summary>An unattended entry has no actor id at all rather than an empty one.</summary>
    [Fact]
    public async Task An_unattended_entry_has_no_actor_id_at_all_rather_than_an_empty_one()
    {
        var audit = new RecordingAuditWriter();
        var journal = new MonitoringAuditJournal(audit, new FakeCurrentUser());

        await journal.RecordSystemAsync(AuditActions.AlertRaised, "DiskFull:/", succeeded: true, CancellationToken.None);

        Assert.Null(Assert.Single(audit.Entries).ActorUserId);
    }

    /// <summary>An unattended entry leaves the origin columns empty rather than naming the panel in them.</summary>
    [Fact]
    public async Task An_unattended_entry_leaves_the_origin_columns_empty_rather_than_naming_the_panel_in_them()
    {
        var audit = new RecordingAuditWriter();
        var journal = new MonitoringAuditJournal(audit, new FakeCurrentUser());

        await journal.RecordSystemAsync(AuditActions.AlertRaised, "DiskFull:/", succeeded: true, CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(string.Empty, entry.IpAddress);
        Assert.Equal(string.Empty, entry.UserAgent);
    }

    /// <summary>A request-driven entry still records the signed-in caller, not the panel.</summary>
    [Fact]
    public async Task A_request_driven_entry_still_records_the_signed_in_caller_not_the_panel()
    {
        var audit = new RecordingAuditWriter();
        var user = new FakeCurrentUser();
        var journal = new MonitoringAuditJournal(audit, user);

        await journal.RecordRequestAsync(
            AuditActions.SmtpSettingsSaved,
            "smtp.example.com",
            "203.0.113.7",
            "Mozilla/5.0",
            succeeded: true,
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(user.UserId, entry.ActorUserId);
        Assert.Equal(user.Username, entry.ActorUsername);
        Assert.Equal("203.0.113.7", entry.IpAddress);
    }
}
