using Maran.Modules.Files.Models;
using Maran.Modules.Files.Services;
using Maran.Modules.Files.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Files.Tests.Services;

/// <summary>Holds <see cref="FilesAccountGate"/> to tenancy and suspension, with a journal entry for each refusal.</summary>
public sealed class FilesAccountGateTests
{
    /// <summary>Where a call came from, for every entry.</summary>
    private static readonly FilesCallOrigin Origin = new("203.0.113.9", "tests");

    /// <summary>The active account the directory knows.</summary>
    private readonly AccountSnapshot _active = FilesTestAccounts.Account(Guid.NewGuid(), "alpha", AccountState.Active);

    /// <summary>The suspended account the directory knows.</summary>
    private readonly AccountSnapshot _suspended =
        FilesTestAccounts.Account(Guid.NewGuid(), "beta", AccountState.Suspended);

    /// <summary>What the journal wrote.</summary>
    private readonly RecordingAuditWriter _writer = new();

    /// <summary>Builds a gate for a caller.</summary>
    /// <param name="caller">Who is asking.</param>
    /// <returns>The gate.</returns>
    private FilesAccountGate GateFor(Caller caller)
    {
        ICurrentUser user = new FakeCurrentUser(caller);
        return new FilesAccountGate(
            new StubAccountDirectory(_active, _suspended), user, new FilesAuditJournal(_writer, user));
    }

    /// <summary>The caller who owns the accounts in these tests.</summary>
    /// <returns>A customer caller.</returns>
    private CustomerCaller Customer()
    {
        return new CustomerCaller(Guid.NewGuid(), "owner", _active.Id);
    }

    /// <summary>An account the caller may see is admitted with its snapshot.</summary>
    [Fact]
    public async Task An_active_account_the_caller_may_see_is_admitted()
    {
        var admitted = await GateFor(Customer()).AdmitAsync(_active.Id, FileAuditActions.FileSaved, Origin, default);

        Assert.True(admitted.IsSuccess);
        Assert.Equal("alpha", admitted.Value!.Username);
        Assert.Empty(_writer.Entries);
    }

    /// <summary>Another tenant's id is a 404 and leaves a failure entry naming the probed id.</summary>
    [Fact]
    public async Task An_account_the_caller_may_not_see_is_not_found_and_the_probe_is_journalled()
    {
        var probed = Guid.NewGuid();

        var refused = await GateFor(Customer()).AdmitAsync(probed, FileAuditActions.FileAccessRefused, Origin, default);

        Assert.Equal("AccountNotFound", refused.Error!.Code);
        Assert.Equal(ErrorType.NotFound, refused.Error.Type);
        var entry = Assert.Single(_writer.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(probed.ToString(), entry.Subject);
        Assert.Null(entry.AccountId);
        Assert.Equal(FileAuditActions.FileAccessRefused, entry.Action);
    }

    /// <summary>A customer on a suspended account is refused with a conflict, and it is journalled.</summary>
    [Fact]
    public async Task A_customer_on_a_suspended_account_is_refused()
    {
        var refused = await GateFor(Customer()).AdmitAsync(_suspended.Id, FileAuditActions.FileDeleted, Origin, default);

        Assert.Equal("FilesAccountSuspended", refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        var entry = Assert.Single(_writer.Entries);
        Assert.Equal("beta:", entry.Subject);
        Assert.Equal(_suspended.Id, entry.AccountId);
    }

    /// <summary>An administrator may still act on a suspended account.</summary>
    [Fact]
    public async Task An_administrator_may_act_on_a_suspended_account()
    {
        var admin = new AdministratorCaller(Guid.NewGuid(), "root");

        var admitted = await GateFor(admin).AdmitAsync(_suspended.Id, FileAuditActions.FileDeleted, Origin, default);

        Assert.True(admitted.IsSuccess);
    }
}
