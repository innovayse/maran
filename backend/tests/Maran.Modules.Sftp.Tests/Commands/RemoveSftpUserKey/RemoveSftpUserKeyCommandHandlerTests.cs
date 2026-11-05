using Maran.Modules.Sftp.Commands.RemoveSftpUserKey;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sftp.Tests.Commands.RemoveSftpUserKey;

/// <summary>
/// What a removal does to the panel, the agent's key file and the login's open sessions, in what
/// order, and what each failure leaves behind (threat note, section 3, and issue #339).
/// </summary>
public sealed class RemoveSftpUserKeyCommandHandlerTests
{
    /// <summary>Removing a key sends the agent the remaining set, ends the sessions and commits, in that order.</summary>
    [Fact]
    public async Task Removing_a_key_replaces_the_set_then_ends_the_sessions_then_commits()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        await AddSecondKeyAsync(world);
        var order = new List<string>();
        var rowsWhenTold = -1;
        world.KeyLock.OnBegin = () =>
        {
            order.Add("lock");
            world.KeyLock.Transactions[^1].OnCommit = () =>
            {
                order.Add("commit");
            };
        };
        world.Agent.OnSetAuthorizedKeys = () =>
        {
            order.Add("set");
            using var read = world.AdminContext();
            rowsWhenTold = read.SftpUserKeys.Count(key => key.SftpUserId == world.OwnLoginId);
        };
        world.Agent.OnEndSessions = () =>
        {
            order.Add("end");
        };

        var result = await RunAsync(world, world.OwnLoginId, world.OwnKeyId);

        Assert.True(result.IsSuccess);
        Assert.Equal(["lock", "set", "end", "commit"], order);
        Assert.Equal(1, rowsWhenTold);
        Assert.True(Assert.Single(world.KeyLock.Transactions).Committed);
        Assert.Equal([world.OwnLoginId], world.KeyLock.Locked);
    }

    /// <summary>The set sent is the complete remaining one, addressed by the row's names.</summary>
    [Fact]
    public async Task The_set_sent_is_the_complete_remaining_one_under_the_names_the_row_recorded()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        await AddSecondKeyAsync(world);

        await RunAsync(world, world.OwnLoginId, world.OwnKeyId);

        var set = Assert.Single(world.Agent.SetAuthorizedKeysCalls);
        Assert.Equal("alice", set.AccountUsername);
        Assert.Equal("deploy", set.SftpUsername);
        Assert.Equal([SftpTestKeys.P384], set.PublicKeys);
        var ended = Assert.Single(world.Agent.EndSessionsCalls);
        Assert.Equal("alice", ended.AccountUsername);
        Assert.Equal("deploy", ended.SftpUsername);
    }

    /// <summary>Removing the last key sends an empty set, which clears the key file.</summary>
    [Fact]
    public async Task Removing_the_last_key_sends_an_empty_set()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await RunAsync(world, world.OwnLoginId, world.OwnKeyId);

        Assert.True(result.IsSuccess);
        Assert.Empty(Assert.Single(world.Agent.SetAuthorizedKeysCalls).PublicKeys);
        using var read = world.AdminContext();
        Assert.Empty(await read.SftpUserKeys.Where(key => key.SftpUserId == world.OwnLoginId).ToListAsync());
    }

    /// <summary>Only the named key is removed; the login's other keys and other logins' keys survive.</summary>
    [Fact]
    public async Task Only_the_named_key_is_removed()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        var second = await AddSecondKeyAsync(world);

        await RunAsync(world, world.OwnLoginId, world.OwnKeyId);

        using var read = world.AdminContext();
        var remaining = await read.SftpUserKeys.Select(key => key.Id).ToListAsync();
        Assert.DoesNotContain(world.OwnKeyId, remaining);
        Assert.Contains(second, remaining);
        Assert.Contains(world.SiblingKeyId, remaining);
        Assert.Contains(world.StrangerKeyId, remaining);
    }

    /// <summary>A refused agent write is not committed, does not end sessions and is told as the agent's error.</summary>
    [Fact]
    public async Task A_refused_agent_write_is_not_committed_and_does_not_end_sessions()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        world.Agent.SetAuthorizedKeysResult = Result.Fail(Error.Of("AgentAccountBusy", ErrorType.Failure));

        var result = await RunAsync(world, world.OwnLoginId, world.OwnKeyId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAccountBusy", result.Error!.Code);
        var transaction = Assert.Single(world.KeyLock.Transactions);
        Assert.False(transaction.Committed);
        Assert.True(transaction.Disposed);
        Assert.Empty(world.Agent.EndSessionsCalls);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(SftpAuditActions.SftpKeyRemoved, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>
    /// Sessions that could not be ended after the set was replaced roll the removal back and answer the
    /// dedicated error, so the revocation is never reported as done.
    /// </summary>
    [Fact]
    public async Task Sessions_that_could_not_be_ended_roll_the_removal_back_and_answer_the_dedicated_error()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        world.Agent.EndSessionsResult = Result<int>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await RunAsync(world, world.OwnLoginId, world.OwnKeyId);

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpSessionsNotEnded", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        var transaction = Assert.Single(world.KeyLock.Transactions);
        Assert.False(transaction.Committed);
        Assert.True(transaction.Disposed);
        Assert.Single(world.Agent.SetAuthorizedKeysCalls);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("deploy | existing | " + SftpTestKeys.Ed25519Fingerprint, entry.Subject);
    }

    /// <summary>A login the host no longer holds has no sessions to end, so the removal still commits.</summary>
    [Fact]
    public async Task A_login_the_host_no_longer_holds_does_not_stop_the_removal()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        world.Agent.EndSessionsResult = Result<int>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await RunAsync(world, world.OwnLoginId, world.OwnKeyId);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(world.KeyLock.Transactions).Committed);
    }

    /// <summary>A commit that fails after both effects took reports the failure and compensates nothing.</summary>
    [Fact]
    public async Task A_commit_that_fails_after_both_effects_took_reports_the_failure_and_compensates_nothing()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        world.KeyLock.CommitFailure = new InvalidOperationException("connection lost");

        var result = await RunAsync(world, world.OwnLoginId, world.OwnKeyId);

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpKeyChangeFailed", result.Error!.Code);
        Assert.False(Assert.Single(world.KeyLock.Transactions).Committed);

        // The host already lacks the key; sending the old set back would restore a revoked credential.
        Assert.Single(world.Agent.SetAuthorizedKeysCalls);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>Another account's login answers not found and touches neither the agent nor the key.</summary>
    [Fact]
    public async Task Another_accounts_login_answers_not_found_and_touches_nothing()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await RunAsync(world, world.StrangerLoginId, world.StrangerKeyId);

        AssertNotFound(world, result, "SftpUserNotFound");
        using var read = world.AdminContext();
        Assert.True(await read.SftpUserKeys.AnyAsync(key => key.Id == world.StrangerKeyId));
        Assert.Equal(world.StrangerLoginId.ToString(), Assert.Single(world.Audit.Entries).Subject);
    }

    /// <summary>A key of another account named under the caller's own login is not found.</summary>
    [Fact]
    public async Task Another_accounts_key_named_under_the_callers_own_login_is_not_found()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await RunAsync(world, world.OwnLoginId, world.StrangerKeyId);

        AssertNotFound(world, result, "SftpKeyNotFound");
        using var read = world.AdminContext();
        Assert.True(await read.SftpUserKeys.AnyAsync(key => key.Id == world.StrangerKeyId));
    }

    /// <summary>A key of the caller's other login named under this login is not found, and survives.</summary>
    [Fact]
    public async Task A_key_of_the_callers_other_login_named_under_this_login_is_not_found()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await RunAsync(world, world.OwnLoginId, world.SiblingKeyId);

        AssertNotFound(world, result, "SftpKeyNotFound");
        using var read = world.AdminContext();
        Assert.True(await read.SftpUserKeys.AnyAsync(key => key.Id == world.SiblingKeyId));
    }

    /// <summary>A key id that names nothing is not found.</summary>
    [Fact]
    public async Task A_key_id_that_names_nothing_is_not_found()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await RunAsync(world, world.OwnLoginId, Guid.NewGuid());

        AssertNotFound(world, result, "SftpKeyNotFound");
    }

    /// <summary>A login id that names nothing answers exactly as another tenant's does.</summary>
    [Fact]
    public async Task A_login_id_that_names_nothing_answers_exactly_as_another_tenants_does()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var result = await RunAsync(world, Guid.NewGuid(), world.OwnKeyId);

        AssertNotFound(world, result, "SftpUserNotFound");
    }

    /// <summary>A login whose account the directory does not know answers account-not-found and calls no agent.</summary>
    [Fact]
    public async Task A_login_whose_account_the_directory_does_not_know_calls_no_agent()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        var handler = NewHandler(world, new StubAccountDirectory());

        var result = await handler.HandleAsync(
            new RemoveSftpUserKeyCommand(world.OwnLoginId, world.OwnKeyId, "203.0.113.7", "tests"),
            CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.SetAuthorizedKeysCalls);
        Assert.Empty(world.Agent.EndSessionsCalls);
    }

    /// <summary>A removal is journalled with the login, label and fingerprint, and never the key body.</summary>
    [Fact]
    public async Task A_removal_is_journalled_with_login_label_and_fingerprint_and_never_the_key_body()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        await RunAsync(world, world.OwnLoginId, world.OwnKeyId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(SftpAuditActions.SftpKeyRemoved, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("deploy | existing | " + SftpTestKeys.Ed25519Fingerprint, entry.Subject);
        Assert.DoesNotContain(SftpTestKeys.Ed25519.Split(' ')[1], entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>Runs one removal as the caller.</summary>
    /// <param name="world">The world to run in.</param>
    /// <param name="loginId">The login named in the route.</param>
    /// <param name="keyId">The key named in the route.</param>
    private static async Task<Result> RunAsync(SftpKeyTestWorld world, Guid loginId, Guid keyId)
    {
        var handler = NewHandler(world, world.Accounts);

        return await handler.HandleAsync(
            new RemoveSftpUserKeyCommand(loginId, keyId, "203.0.113.7", "tests"),
            CancellationToken.None);
    }

    /// <summary>Builds the handler over the world's doubles.</summary>
    /// <param name="world">The world.</param>
    /// <param name="accounts">The account directory to use.</param>
    private static RemoveSftpUserKeyCommandHandler NewHandler(SftpKeyTestWorld world, StubAccountDirectory accounts)
    {
        return new RemoveSftpUserKeyCommandHandler(
            world.CallerContext(),
            accounts,
            world.Agent,
            world.KeyLock,
            world.Journal);
    }

    /// <summary>Adds a second key (P-384) to the caller's <c>deploy</c> login.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The new key's id.</returns>
    private static async Task<Guid> AddSecondKeyAsync(SftpKeyTestWorld world)
    {
        using var context = world.AdminContext();
        var login = await context.SftpUsers.Include(row => row.Keys).SingleAsync(row => row.Id == world.OwnLoginId);
        var added = login.AddKey(
            Guid.NewGuid(),
            "second",
            SftpPublicKey.Parse(SftpTestKeys.P384).Value,
            SftpKeyTestWorld.Epoch.AddDays(1));
        await context.SaveChangesAsync();

        Assert.True(added.IsSuccess);

        return added.Value.Id;
    }

    /// <summary>Asserts a not-found answer that touched neither the agent nor a lock's commit.</summary>
    /// <param name="world">The world.</param>
    /// <param name="result">What the handler answered.</param>
    /// <param name="code">The expected code.</param>
    private static void AssertNotFound(SftpKeyTestWorld world, Result result, string code)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.SetAuthorizedKeysCalls);
        Assert.Empty(world.Agent.EndSessionsCalls);
        Assert.All(world.KeyLock.Transactions, transaction =>
        {
            Assert.False(transaction.Committed);
        });
    }
}
