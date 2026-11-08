using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Which part of the journal each caller may read: everything for an administrator, its own accounts
/// and login for a reseller, and a refusal for every other caller — so a reader dispatched by a caller
/// nobody planned for is never handed the whole journal.
/// </summary>
public sealed class AuditEventScopeReaderTests
{
    /// <summary>An administrator reads every entry.</summary>
    [Fact]
    public async Task An_administrator_reads_every_entry()
    {
        var result = await ReaderFor(new StubCurrentUser(), new StubResellerDirectory()).ReadAsync(CancellationToken.None);

        Assert.IsType<AllEntriesScope>(result.Value);
    }

    /// <summary>A reseller's scope is the accounts the Accounts module says it owns, plus its own login.</summary>
    [Fact]
    public async Task A_reseller_reads_its_own_accounts_and_its_own_login()
    {
        var directory = new StubResellerDirectory();
        var owned = Guid.NewGuid();
        directory.AddOwnedAccount(owned);
        var loginId = Guid.NewGuid();

        var result = await ReaderFor(new StubResellerUser(loginId, "reseller", Guid.NewGuid()), directory)
            .ReadAsync(CancellationToken.None);

        var scope = Assert.IsType<ResellerEntriesScope>(result.Value);
        Assert.Equal(loginId, scope.LoginUserId);
        Assert.True(scope.Owns(owned));
        Assert.False(scope.Owns(Guid.NewGuid()));
    }

    /// <summary>A reseller that owns no account still has a scope, and it holds nothing beyond its login.</summary>
    [Fact]
    public async Task A_reseller_owning_nothing_has_an_empty_account_scope_and_not_the_whole_journal()
    {
        var result = await ReaderFor(new StubResellerUser(Guid.NewGuid(), "reseller", Guid.NewGuid()), new StubResellerDirectory())
            .ReadAsync(CancellationToken.None);

        var scope = Assert.IsType<ResellerEntriesScope>(result.Value);
        Assert.Empty(scope.OwnedAccountIds);
    }

    /// <summary>Every other caller is refused, as Forbidden — the fail-closed second line behind the controllers' policies.</summary>
    [Fact]
    public async Task Every_other_caller_is_refused()
    {
        var customer = new StubCustomerUser(Guid.NewGuid(), "customer", Guid.NewGuid());
        var accountless = new StubCustomerUser(Guid.NewGuid(), "customer", null);

        foreach (var user in new ICurrentUser[] { customer, accountless, new FixedCaller(AnonymousCaller.Instance), new FixedCaller(new ApiKeyCaller(Guid.NewGuid(), ["read"])) })
        {
            var result = await ReaderFor(user, new StubResellerDirectory()).ReadAsync(CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("AuditJournalNotReadable", result.Error!.Code);
            Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        }
    }

    /// <summary>Creates the reader for a caller and a directory.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="directory">The Accounts window.</param>
    /// <returns>The reader.</returns>
    private static AuditEventScopeReader ReaderFor(ICurrentUser user, StubResellerDirectory directory)
    {
        return new AuditEventScopeReader(user, directory);
    }

    /// <summary>An <see cref="ICurrentUser"/> naming exactly the caller it was given.</summary>
    private sealed class FixedCaller : ICurrentUser
    {
        /// <summary>Creates the principal.</summary>
        /// <param name="caller">The caller to report.</param>
        public FixedCaller(Caller caller)
        {
            Caller = caller;
        }

        /// <inheritdoc />
        public Caller Caller { get; }
    }
}
