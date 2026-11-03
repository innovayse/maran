using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Services;

/// <summary>What <see cref="ResellerScope"/> says about the caller's reseller identity and a suspended one.</summary>
public sealed class ResellerScopeTests
{
    /// <summary>A reseller caller's reseller id is the one its token names.</summary>
    [Fact]
    public void A_reseller_caller_acts_for_the_reseller_its_token_names()
    {
        var resellerId = Guid.NewGuid();

        var scope = ScopeFor(FakeCurrentUser.Reseller(resellerId));

        Assert.Equal(resellerId, scope.CallerResellerId);
    }

    /// <summary>No other kind of caller is a reseller.</summary>
    [Fact]
    public void No_other_kind_of_caller_acts_for_a_reseller()
    {
        Assert.Null(ScopeFor(FakeCurrentUser.Admin()).CallerResellerId);
        Assert.Null(ScopeFor(FakeCurrentUser.Customer(Guid.NewGuid())).CallerResellerId);
        Assert.Null(ScopeFor(FakeCurrentUser.AccountlessCustomer()).CallerResellerId);
        Assert.Null(ScopeFor(FakeCurrentUser.Anonymous()).CallerResellerId);
    }

    /// <summary>A reseller always creates for itself when it names no owner.</summary>
    [Fact]
    public void A_reseller_that_names_no_owner_creates_for_itself()
    {
        var resellerId = Guid.NewGuid();

        var result = ScopeFor(FakeCurrentUser.Reseller(resellerId)).ResolveOwner(null);

        Assert.True(result.IsSuccess);
        Assert.Equal(resellerId, result.Value);
    }

    /// <summary>A reseller naming itself as the owner creates for itself.</summary>
    [Fact]
    public void A_reseller_that_names_itself_creates_for_itself()
    {
        var resellerId = Guid.NewGuid();

        var result = ScopeFor(FakeCurrentUser.Reseller(resellerId)).ResolveOwner(resellerId);

        Assert.True(result.IsSuccess);
        Assert.Equal(resellerId, result.Value);
    }

    /// <summary>A reseller naming another reseller is answered exactly as for one that does not exist.</summary>
    [Fact]
    public void A_reseller_that_names_another_owner_is_told_the_reseller_is_not_found()
    {
        var result = ScopeFor(FakeCurrentUser.Reseller(Guid.NewGuid())).ResolveOwner(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>An administrator creates for the reseller it names, or for itself when it names none.</summary>
    [Fact]
    public void An_administrator_creates_for_the_reseller_it_names_or_for_itself()
    {
        var scope = ScopeFor(FakeCurrentUser.Admin());
        var named = Guid.NewGuid();

        Assert.Equal(named, scope.ResolveOwner(named).Value);
        Assert.Null(scope.ResolveOwner(null).Value);
    }

    /// <summary>A caller that is no reseller is never refused for suspension, and nothing is read.</summary>
    [Fact]
    public async Task A_caller_that_is_no_reseller_is_never_refused_for_suspension()
    {
        var scope = ScopeFor(FakeCurrentUser.Admin());

        Assert.Null(await scope.RequireActiveAsync(CancellationToken.None));
    }

    /// <summary>An active reseller may write.</summary>
    [Fact]
    public async Task An_active_reseller_may_write()
    {
        var reseller = ResellerTestData.Reseller();
        var (scope, dbContext) = ScopeWith(reseller);
        await using var _ = dbContext;

        Assert.Null(await scope.RequireActiveAsync(CancellationToken.None));
    }

    /// <summary>A suspended reseller's live token is refused with a conflict.</summary>
    [Fact]
    public async Task A_suspended_reseller_is_refused_with_a_conflict()
    {
        var reseller = ResellerTestData.Reseller();
        reseller.Suspend();
        var (scope, dbContext) = ScopeWith(reseller);
        await using var _ = dbContext;

        var error = await scope.RequireActiveAsync(CancellationToken.None);

        Assert.Equal("ResellerSuspended", error!.Code);
        Assert.Equal(ErrorType.Conflict, error.Type);
    }

    /// <summary>A reseller whose row is gone is refused as not found.</summary>
    [Fact]
    public async Task A_reseller_whose_row_is_gone_is_refused_as_not_found()
    {
        var currentUser = FakeCurrentUser.Reseller(Guid.NewGuid());
        await using var dbContext = AccountsTestContext.Create(currentUser);
        var scope = new ResellerScope(dbContext, currentUser);

        var error = await scope.RequireActiveAsync(CancellationToken.None);

        Assert.Equal("ResellerNotFound", error!.Code);
        Assert.Equal(ErrorType.NotFound, error.Type);
    }

    /// <summary>
    /// A reseller whose caller token names ANOTHER reseller's row sees only its own id: the status read
    /// is by the caller's id, never by anything else.
    /// </summary>
    [Fact]
    public async Task The_status_read_is_for_the_callers_own_reseller_and_no_other()
    {
        var suspendedOther = ResellerTestData.Reseller("other");
        suspendedOther.Suspend();
        var own = ResellerTestData.Reseller("mine");
        var currentUser = FakeCurrentUser.Reseller(own.Id);
        await using var dbContext = AccountsTestContext.Create(currentUser);
        dbContext.Resellers.AddRange(own, suspendedOther);
        await dbContext.SaveChangesAsync();
        var scope = new ResellerScope(dbContext, currentUser);

        Assert.Null(await scope.RequireActiveAsync(CancellationToken.None));
    }

    /// <summary>Builds a scope for a principal over a fresh, empty context.</summary>
    /// <param name="currentUser">The principal.</param>
    /// <returns>The scope.</returns>
    private static ResellerScope ScopeFor(FakeCurrentUser currentUser)
    {
        return new ResellerScope(AccountsTestContext.Create(currentUser), currentUser);
    }

    /// <summary>Builds a scope for the reseller's own login over a context holding the reseller.</summary>
    /// <param name="reseller">The reseller to seed.</param>
    /// <returns>The scope and the context it reads, which the caller disposes.</returns>
    private static (ResellerScope Scope, AccountsDbContext Context) ScopeWith(Reseller reseller)
    {
        var currentUser = FakeCurrentUser.Reseller(reseller.Id);
        var dbContext = AccountsTestContext.Create(currentUser);
        dbContext.Resellers.Add(reseller);
        dbContext.SaveChanges();

        return (new ResellerScope(dbContext, currentUser), dbContext);
    }
}
