using Maran.Modules.Identity.Commands.EndViewAs;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Identity.Tests.Commands.EndViewAs;

/// <summary>Behavioural contract of the banner's Back, for both kinds of view-as (issues #317, #179).</summary>
public sealed class EndViewAsCommandHandlerTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    private EndViewAsCommandHandler NewHandler()
    {
        return new EndViewAsCommandHandler(_fixture.Context, _fixture.NewSubjectReader(), _fixture.Journal, _fixture.Clock);
    }

    private async Task<(User Admin, User Owner, Session ViewAs)> SeedViewAsAsync()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme");
        var viewAs = Session.StartViewAs(
            Guid.NewGuid(),
            owner.Id,
            accountId,
            admin.Id,
            adminSession.Id,
            Guid.NewGuid().ToString("N"),
            ViewAsFixture.Now,
            "198.51.100.1",
            "admin-agent");
        _fixture.Context.Sessions.Add(viewAs);
        await _fixture.Context.SaveChangesAsync();
        return (admin, owner, viewAs);
    }

    /// <summary>Back revokes the view-as session and journals the end with the administrator as actor.</summary>
    [Fact]
    public async Task Back_revokes_the_view_as_session_and_journals_the_administrator()
    {
        var (admin, owner, viewAs) = await SeedViewAsAsync();

        var result = await NewHandler().HandleAsync(
            new EndViewAsCommand(viewAs.Id, owner.Id, "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.Id == viewAs.Id);
        Assert.Equal(SessionRevocationReason.ViewAsEnded, stored.Revocation?.Reason);

        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.Equal(IdentityAuditActions.ViewAsEnded, entry.Action);
        Assert.Equal(admin.Id, entry.ActorUserId);
        Assert.Equal(admin.Username, entry.ActorUsername);
        Assert.Equal($"account acme as {owner.Username}", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Back from a reseller view revokes the session and journals the reseller as the subject.</summary>
    [Fact]
    public async Task Back_from_a_reseller_view_revokes_the_session_and_names_the_reseller()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (_, login) = await _fixture.SeedResellerAsync("northwind");
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, login, accountId: null);

        var result = await NewHandler().HandleAsync(
            new EndViewAsCommand(viewAs.Id, login.Id, "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await _fixture.Context.Sessions.SingleAsync(s => s.Id == viewAs.Id);
        Assert.Equal(SessionRevocationReason.ViewAsEnded, stored.Revocation?.Reason);
        var entry = Assert.Single(_fixture.Audit.Written);
        Assert.Equal(IdentityAuditActions.ViewAsEnded, entry.Action);
        Assert.Equal(admin.Id, entry.ActorUserId);
        Assert.Equal($"reseller northwind as {login.Username}", entry.Subject);
    }

    /// <summary>Back ends the operator's view only: the operator's own session is left alone.</summary>
    [Fact]
    public async Task Back_leaves_the_operators_own_session_untouched()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (_, login) = await _fixture.SeedResellerAsync();
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, login, accountId: null);

        await NewHandler().HandleAsync(
            new EndViewAsCommand(viewAs.Id, login.Id, "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.Null((await _fixture.Context.Sessions.SingleAsync(s => s.Id == adminSession.Id)).Revocation);
    }

    /// <summary>A session whose facts no longer describe an allowed kind is still ended and journalled by session id.</summary>
    /// <remarks>
    /// An administrator is never a viewed login; a row that says otherwise cannot be named as an
    /// account or a reseller, but ending it must not be refused, because refusing would leave a
    /// session nobody can end.
    /// </remarks>
    [Fact]
    public async Task A_session_that_describes_no_allowed_kind_is_still_ended_and_journalled_by_session_id()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync("root-admin");
        var (otherAdmin, _) = await _fixture.SeedAdminAsync("other-admin");
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, otherAdmin, accountId: null);

        var result = await NewHandler().HandleAsync(
            new EndViewAsCommand(viewAs.Id, otherAdmin.Id, "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull((await _fixture.Context.Sessions.SingleAsync(s => s.Id == viewAs.Id)).Revocation);
        Assert.Equal($"session {viewAs.Id} as {otherAdmin.Username}", Assert.Single(_fixture.Audit.Written).Subject);
    }

    /// <summary>A view-as session of another login cannot be ended with somebody else's login id.</summary>
    [Fact]
    public async Task A_session_cannot_be_ended_under_a_different_viewed_login()
    {
        var (_, _, viewAs) = await SeedViewAsAsync();

        var result = await NewHandler().HandleAsync(
            new EndViewAsCommand(viewAs.Id, Guid.NewGuid(), "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("ViewAsNotViewing", result.Error!.Code);
        Assert.Null((await _fixture.Context.Sessions.SingleAsync(s => s.Id == viewAs.Id)).Revocation);
        Assert.Empty(_fixture.Audit.Written);
    }

    /// <summary>An own session has nothing to go back from, and nothing is revoked or journalled.</summary>
    [Fact]
    public async Task An_own_session_is_not_viewing_and_nothing_changes()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();

        var result = await NewHandler().HandleAsync(
            new EndViewAsCommand(adminSession.Id, admin.Id, "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Null((await _fixture.Context.Sessions.SingleAsync(s => s.Id == adminSession.Id)).Revocation);
        Assert.Empty(_fixture.Audit.Written);
    }

    /// <summary>An expired view-as session cannot be ended a second time and journals nothing.</summary>
    [Fact]
    public async Task An_expired_view_as_session_is_not_viewing()
    {
        var (_, owner, viewAs) = await SeedViewAsAsync();
        _fixture.Clock.Advance(TimeSpan.FromMinutes(16));

        var result = await NewHandler().HandleAsync(
            new EndViewAsCommand(viewAs.Id, owner.Id, "198.51.100.1", "admin-agent"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(_fixture.Audit.Written);
    }
}
