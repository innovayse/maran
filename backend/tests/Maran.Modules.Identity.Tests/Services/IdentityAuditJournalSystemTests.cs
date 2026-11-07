using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>What the module's journal writes for work the panel did on its own initiative.</summary>
public sealed class IdentityAuditJournalSystemTests
{
    /// <summary>The recorded entry names the panel as the actor and has no origin, because nothing arrived over HTTP.</summary>
    [Fact]
    public async Task A_system_entry_names_the_panel_and_carries_no_origin()
    {
        var audit = new RecordingAuditWriter();

        await Journal(audit).RecordSystemAsync(
            IdentityAuditActions.PersonalDataErased, "erased-user-1a2b3c4d rows=3", succeeded: true, CancellationToken.None);

        var entry = Assert.Single(audit.Written);
        Assert.Null(entry.ActorUserId);
        Assert.Equal("maran-identity", entry.ActorUsername);
        Assert.Equal(IdentityAuditActions.PersonalDataErased, entry.Action);
        Assert.Equal("erased-user-1a2b3c4d rows=3", entry.Subject);
        Assert.Equal(string.Empty, entry.IpAddress);
        Assert.Equal(string.Empty, entry.UserAgent);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refused unattended run is recorded as failed, which is how it is told apart from a done one.</summary>
    [Fact]
    public async Task A_refused_system_run_is_recorded_as_failed()
    {
        var audit = new RecordingAuditWriter();

        await Journal(audit).RecordSystemAsync(
            IdentityAuditActions.PersonalDataErased, "refused", succeeded: false, CancellationToken.None);

        Assert.False(Assert.Single(audit.Written).Succeeded);
    }

    /// <summary>The signed-in caller does not leak into a system entry: the actor is the panel even when a person triggered the run.</summary>
    [Fact]
    public async Task A_system_entry_never_takes_its_actor_from_the_signed_in_caller()
    {
        var audit = new RecordingAuditWriter();
        var caller = new StubCurrentUser(Guid.NewGuid(), "some-administrator");

        await new IdentityAuditJournal(audit, caller).RecordSystemAsync(
            IdentityAuditActions.PersonalDataErased, "subject", succeeded: true, CancellationToken.None);

        var entry = Assert.Single(audit.Written);
        Assert.Null(entry.ActorUserId);
        Assert.Equal("maran-identity", entry.ActorUsername);
    }

    /// <summary>Builds a journal over <paramref name="audit"/> with an administrator as the current principal.</summary>
    /// <param name="audit">Where the entries are kept.</param>
    /// <returns>The journal under test.</returns>
    private static IdentityAuditJournal Journal(RecordingAuditWriter audit)
    {
        return ErasureHandlers.Journal(audit);
    }
}
