using Maran.Modules.Ssl.Services;
using Maran.Modules.Ssl.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl.Tests.Services;

/// <summary>
/// How this module records an unattended renewal: one shared spelling of the system actor, and no
/// invented request origin.
/// </summary>
public sealed class CertificateAuditJournalTests
{
    /// <summary>An unattended entry names the panel through the one shared definition.</summary>
    [Fact]
    public async Task An_unattended_entry_names_the_panel_through_the_one_shared_definition()
    {
        var audit = new RecordingAuditWriter();
        var journal = new CertificateAuditJournal(audit, FakeCurrentUser.Admin());

        await journal.RecordScheduledAsync(
            AuditActions.CertificateRenewed,
            "soon.example.com",
            succeeded: true,
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(SystemAuditEntry.NameFor(CertificateAuditJournal.ModuleName), entry.ActorUsername);
    }

    /// <summary>An unattended entry has no actor id at all rather than an empty one.</summary>
    [Fact]
    public async Task An_unattended_entry_has_no_actor_id_at_all_rather_than_an_empty_one()
    {
        var audit = new RecordingAuditWriter();
        var journal = new CertificateAuditJournal(audit, FakeCurrentUser.Admin());

        await journal.RecordScheduledAsync(
            AuditActions.CertificateRenewed,
            "soon.example.com",
            succeeded: true,
            CancellationToken.None);

        Assert.Null(Assert.Single(audit.Entries).ActorUserId);
    }

    /// <summary>An unattended entry leaves the origin columns empty rather than naming the panel in them.</summary>
    [Fact]
    public async Task An_unattended_entry_leaves_the_origin_columns_empty_rather_than_naming_the_panel_in_them()
    {
        var audit = new RecordingAuditWriter();
        var journal = new CertificateAuditJournal(audit, FakeCurrentUser.Admin());

        await journal.RecordScheduledAsync(
            AuditActions.CertificateRenewed,
            "soon.example.com",
            succeeded: false,
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(string.Empty, entry.IpAddress);
        Assert.Equal(string.Empty, entry.UserAgent);
    }

    /// <summary>Every module's system actor name is built from the one prefix.</summary>
    [Fact]
    public void Every_modules_system_actor_name_is_built_from_the_one_prefix()
    {
        Assert.Equal("maran-ssl", SystemAuditEntry.NameFor(CertificateAuditJournal.ModuleName));
        Assert.StartsWith(SystemAuditEntry.NamePrefix, SystemAuditEntry.NameFor("firewall"), StringComparison.Ordinal);
    }

    /// <summary>A request-driven entry still records the signed-in caller, not the panel.</summary>
    [Fact]
    public async Task A_request_driven_entry_still_records_the_signed_in_caller_not_the_panel()
    {
        var audit = new RecordingAuditWriter();
        var user = FakeCurrentUser.Admin();
        var journal = new CertificateAuditJournal(audit, user);

        await journal.RecordSuccessAsync(
            AuditActions.CertificateIssued,
            "a.example.com",
            "203.0.113.7",
            "Mozilla/5.0",
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(user.UserId, entry.ActorUserId);
        Assert.Equal("203.0.113.7", entry.IpAddress);
    }
}
