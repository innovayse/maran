using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Queries.GetViewAs;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Queries.GetViewAs;

/// <summary>
/// Behavioural contract of the banner's read for both kinds of view-as (issues #317, #314, #179): it
/// answers from the session row, and a session that is not a live, coherent view answers "not viewing".
/// </summary>
public sealed class GetViewAsQueryHandlerTests : IDisposable
{
    private readonly ViewAsFixture _fixture = new();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _fixture.Dispose();
    }

    private GetViewAsQueryHandler NewHandler()
    {
        return new GetViewAsQueryHandler(
            _fixture.Context,
            _fixture.NewVerifier(),
            _fixture.NewSubjectReader());
    }

    /// <summary>An account view reports the account as the subject, with the viewed login and the operator.</summary>
    [Fact]
    public async Task An_account_view_reports_the_account_the_login_and_the_operator()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme");
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, owner, accountId);

        var result = await NewHandler().HandleAsync(new GetViewAsQuery(viewAs.Id, owner.Id), CancellationToken.None);

        var viewing = result.Value.Viewing;
        Assert.NotNull(viewing);
        Assert.Equal(ViewAsKind.Account, viewing.Kind);
        Assert.Equal(accountId, viewing.SubjectId);
        Assert.Equal("acme", viewing.SubjectName);
        Assert.Equal(owner.Username, viewing.Username);
        Assert.Equal(admin.Username, viewing.OperatorUsername);
        Assert.Equal(viewAs.ExpiresAt, viewing.ExpiresAt);
    }

    /// <summary>A reseller view reports the reseller, not its login, as the subject.</summary>
    [Fact]
    public async Task A_reseller_view_reports_the_reseller_and_not_its_login_as_the_subject()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (resellerId, login) = await _fixture.SeedResellerAsync("northwind");
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, login, accountId: null);

        var result = await NewHandler().HandleAsync(new GetViewAsQuery(viewAs.Id, login.Id), CancellationToken.None);

        var viewing = result.Value.Viewing;
        Assert.NotNull(viewing);
        Assert.Equal(ViewAsKind.Reseller, viewing.Kind);
        Assert.Equal(resellerId, viewing.SubjectId);
        Assert.NotEqual(login.Id, viewing.SubjectId);
        Assert.Equal("northwind", viewing.SubjectName);
        Assert.Equal(login.Username, viewing.Username);
        Assert.Equal(admin.Username, viewing.OperatorUsername);
    }

    /// <summary>A subject the directory no longer knows is named by its id, so the banner still reads.</summary>
    [Fact]
    public async Task A_subject_the_directory_no_longer_knows_is_named_by_its_id()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (accountId, owner) = await _fixture.SeedAccountAsync("acme");
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, owner, Guid.NewGuid());

        var result = await NewHandler().HandleAsync(new GetViewAsQuery(viewAs.Id, owner.Id), CancellationToken.None);

        var viewing = result.Value.Viewing;
        Assert.NotNull(viewing);
        Assert.Equal(viewing.SubjectId.ToString(), viewing.SubjectName);
        Assert.NotEqual(accountId, viewing.SubjectId);
    }

    /// <summary>The operator's own session is not a view: the answer is no viewing, not a failure.</summary>
    [Fact]
    public async Task An_own_session_answers_not_viewing()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();

        var result = await NewHandler().HandleAsync(new GetViewAsQuery(adminSession.Id, admin.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Viewing);
    }

    /// <summary>A view that has been ended answers not viewing even to a token that still carries the markers.</summary>
    [Fact]
    public async Task An_ended_view_answers_not_viewing()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (_, login) = await _fixture.SeedResellerAsync();
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, login, accountId: null);
        viewAs.Revoke(ViewAsFixture.Now, SessionRevocationReason.ViewAsEnded);
        await _fixture.Context.SaveChangesAsync();

        var result = await NewHandler().HandleAsync(new GetViewAsQuery(viewAs.Id, login.Id), CancellationToken.None);

        Assert.Null(result.Value.Viewing);
    }

    /// <summary>A view past its lifetime answers not viewing.</summary>
    [Fact]
    public async Task An_expired_view_answers_not_viewing()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (_, login) = await _fixture.SeedResellerAsync();
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, login, accountId: null);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(15));

        var result = await NewHandler().HandleAsync(new GetViewAsQuery(viewAs.Id, login.Id), CancellationToken.None);

        Assert.Null(result.Value.Viewing);
    }

    /// <summary>A token whose subject is somebody else's login does not read another login's view.</summary>
    [Fact]
    public async Task A_query_naming_a_different_viewed_login_answers_not_viewing()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync();
        var (_, login) = await _fixture.SeedResellerAsync();
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, login, accountId: null);

        var result = await NewHandler().HandleAsync(new GetViewAsQuery(viewAs.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result.Value.Viewing);
    }

    /// <summary>A session whose facts describe no allowed kind never produces a banner.</summary>
    [Fact]
    public async Task A_session_of_an_administrator_login_answers_not_viewing()
    {
        var (admin, adminSession) = await _fixture.SeedAdminAsync("root-admin");
        var (otherAdmin, _) = await _fixture.SeedAdminAsync("other-admin");
        var viewAs = await _fixture.AddViewAsSessionAsync(admin, adminSession, otherAdmin, accountId: null);

        var result = await NewHandler().HandleAsync(new GetViewAsQuery(viewAs.Id, otherAdmin.Id), CancellationToken.None);

        Assert.Null(result.Value.Viewing);
    }
}
