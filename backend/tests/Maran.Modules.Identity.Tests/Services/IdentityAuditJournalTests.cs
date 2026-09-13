using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// How this module separates the identity a caller CLAIMED from the identity the panel VERIFIED,
/// and what it records when the caller offered neither.
/// </summary>
public sealed class IdentityAuditJournalTests
{
    private const string Address = "203.0.113.7";
    private const string Client = "Mozilla/5.0";

    private readonly RecordingAuditWriter _audit = new();

    /// <summary>A claimed name is recorded even when it matched nobody, because that is the sweep.</summary>
    [Fact]
    public async Task A_claimed_name_is_recorded_even_when_it_matched_nobody()
    {
        await NewJournal(new StubCurrentUser()).RecordClaimAsync(
            null,
            "nosuchuser",
            AuditActions.LoginFailed,
            Address,
            Client,
            succeeded: false,
            CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal("nosuchuser", entry.ActorUsername);
        Assert.Null(entry.ActorUserId);
    }

    /// <summary>A claim that matched a real user records that user as the verified actor.</summary>
    [Fact]
    public async Task A_claim_that_matched_a_real_user_records_that_user_as_the_verified_actor()
    {
        var matched = Guid.NewGuid();

        await NewJournal(new StubCurrentUser()).RecordClaimAsync(
            matched,
            "operator",
            AuditActions.LoginSucceeded,
            Address,
            Client,
            succeeded: true,
            CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(matched, entry.ActorUserId);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The claimed name is the subject too, because a sign-in acts on the account it names.</summary>
    [Fact]
    public async Task The_claimed_name_is_the_subject_too()
    {
        await NewJournal(new StubCurrentUser()).RecordClaimAsync(
            null,
            "victim@example.com",
            AuditActions.PasswordResetRequested,
            Address,
            Client,
            succeeded: true,
            CancellationToken.None);

        Assert.Equal("victim@example.com", Assert.Single(_audit.Written).Subject);
    }

    /// <summary>An entry never invents an origin: the caller's own address and client are recorded.</summary>
    [Fact]
    public async Task An_entry_records_the_callers_own_address_and_client()
    {
        await NewJournal(new StubCurrentUser()).RecordClaimAsync(
            null,
            "nosuchuser",
            AuditActions.LoginFailed,
            Address,
            Client,
            succeeded: false,
            CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(Address, entry.IpAddress);
        Assert.Equal(Client, entry.UserAgent);
    }

    /// <summary>An identified actor is named when the request's principal is that same user.</summary>
    [Fact]
    public async Task An_identified_actor_is_named_when_the_principal_is_that_same_user()
    {
        var actor = Guid.NewGuid();

        await NewJournal(new StubCurrentUser(actor, "owner")).RecordIdentifiedAsync(
            actor,
            AuditActions.SessionRevoked,
            "a-session",
            Address,
            Client,
            succeeded: true,
            CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(actor, entry.ActorUserId);
        Assert.Equal("owner", entry.ActorUsername);
    }

    /// <summary>An identified actor is left unnamed rather than named after a different principal.</summary>
    [Fact]
    public async Task An_identified_actor_is_left_unnamed_rather_than_named_after_a_different_principal()
    {
        var actor = Guid.NewGuid();

        await NewJournal(new StubCurrentUser(Guid.NewGuid(), "somebody-else")).RecordIdentifiedAsync(
            actor,
            AuditActions.LoggedOut,
            actor.ToString(),
            Address,
            Client,
            succeeded: true,
            CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(actor, entry.ActorUserId);
        Assert.Equal(string.Empty, entry.ActorUsername);
    }

    /// <summary>An identified actor is left unnamed when the endpoint had no principal at all.</summary>
    [Fact]
    public async Task An_identified_actor_is_left_unnamed_when_the_endpoint_had_no_principal_at_all()
    {
        var actor = Guid.NewGuid();

        await NewJournal(new StubCurrentUser(Guid.Empty, string.Empty)).RecordIdentifiedAsync(
            actor,
            AuditActions.LoggedOut,
            actor.ToString(),
            Address,
            Client,
            succeeded: true,
            CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(_audit.Written).ActorUsername);
    }

    /// <summary>A caller who claimed nothing nameable is recorded against the panel itself.</summary>
    [Fact]
    public async Task A_caller_who_claimed_nothing_nameable_is_recorded_against_the_panel_itself()
    {
        await NewJournal(new StubCurrentUser()).RecordUnidentifiedAsync(
            AuditActions.RefreshTokenReuseDetected,
            string.Empty,
            Address,
            Client,
            CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(SystemAuditEntry.NameFor(IdentityAuditJournal.ModuleName), entry.ActorUsername);
        Assert.Null(entry.ActorUserId);
    }

    /// <summary>An unidentified caller still leaves the address behind, unlike an unattended entry.</summary>
    /// <remarks>
    /// Sibling modules blank the origin columns for work the panel did on a timer, because there was
    /// no request. These events DID arrive over HTTP, and the address is the only thing the entry has.
    /// </remarks>
    [Fact]
    public async Task An_unidentified_caller_still_leaves_the_address_behind()
    {
        await NewJournal(new StubCurrentUser()).RecordUnidentifiedAsync(
            AuditActions.PasswordResetRefused,
            string.Empty,
            Address,
            Client,
            CancellationToken.None);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(Address, entry.IpAddress);
        Assert.Equal(Client, entry.UserAgent);
    }

    /// <summary>An unidentified caller is always recorded as refused.</summary>
    [Fact]
    public async Task An_unidentified_caller_is_always_recorded_as_refused()
    {
        await NewJournal(new StubCurrentUser()).RecordUnidentifiedAsync(
            AuditActions.RefreshTokenReuseDetected,
            string.Empty,
            Address,
            Client,
            CancellationToken.None);

        Assert.False(Assert.Single(_audit.Written).Succeeded);
    }

    private IdentityAuditJournal NewJournal(StubCurrentUser principal)
    {
        return new IdentityAuditJournal(_audit, principal);
    }
}
