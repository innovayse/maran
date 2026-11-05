using Maran.Modules.Sftp.Commands.AddSftpUserKey;
using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Domain.Policies;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Maran.Modules.Sftp.Tests.Commands.AddSftpUserKey;

/// <summary>
/// What an add does to the panel, the agent and the journal, in what order, and what each failure
/// leaves behind (threat note, section 3).
/// </summary>
public sealed class AddSftpUserKeyCommandHandlerTests
{
    /// <summary>A comment on the pasted line that must never be stored, logged or audited.</summary>
    private const string SecretComment = "private-comment@workstation";

    /// <summary>The instant the handler's clock reports.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Adding a key answers the new key without its body and stores the bare one-line form.</summary>
    [Fact]
    public async Task Adding_a_key_answers_the_new_key_without_its_body_and_stores_the_bare_line()
    {
        var run = await RunAsync(SftpTestKeys.P256 + " " + SecretComment, "work laptop");

        Assert.True(run.Result.IsSuccess);
        Assert.Equal("work laptop", run.Result.Value.Label);
        Assert.Equal("ecdsa-sha2-nistp256", run.Result.Value.Algorithm);
        Assert.Equal(SftpTestKeys.P256Fingerprint, run.Result.Value.Fingerprint);
        Assert.Equal(Now, run.Result.Value.CreatedAt);
        using var read = run.World.AdminContext();
        var stored = await read.SftpUserKeys.SingleAsync(key => key.Id == run.Result.Value.Id);
        Assert.Equal(SftpTestKeys.P256, stored.PublicKey);
        Assert.Equal(run.World.OwnLoginId, stored.SftpUserId);
    }

    /// <summary>The agent is sent the login's complete set, existing keys first, under the row's names.</summary>
    [Fact]
    public async Task The_agent_is_sent_the_logins_complete_set_under_the_names_the_row_recorded()
    {
        var run = await RunAsync(SftpTestKeys.P256, "second");

        var call = Assert.Single(run.World.Agent.SetAuthorizedKeysCalls);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal("deploy", call.SftpUsername);
        Assert.Equal([SftpTestKeys.Ed25519, SftpTestKeys.P256], call.PublicKeys);
    }

    /// <summary>The login's lock is taken for the right login, and adding never ends sessions.</summary>
    [Fact]
    public async Task The_logins_lock_is_taken_and_adding_never_ends_sessions()
    {
        var run = await RunAsync(SftpTestKeys.P256, "second");

        Assert.Equal([run.World.OwnLoginId], run.World.KeyLock.Locked);
        Assert.Empty(run.World.Agent.EndSessionsCalls);
    }

    /// <summary>The row is stored before the agent is told, and the commit comes only after the agent accepted.</summary>
    [Fact]
    public async Task The_row_is_stored_before_the_agent_is_told_and_committed_only_after_it_accepted()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        var order = new List<string>();
        var rowsWhenTold = -1;
        world.KeyLock.OnBegin = () =>
        {
            order.Add("lock");
        };
        world.Agent.OnSetAuthorizedKeys = () =>
        {
            order.Add("agent");
            Assert.False(Assert.Single(world.KeyLock.Transactions).Committed);
            using var read = world.AdminContext();
            rowsWhenTold = read.SftpUserKeys.Count(key => key.SftpUserId == world.OwnLoginId);
        };

        await RunAsync(world, SftpTestKeys.P256, "second", transactionOnCommit: () =>
        {
            order.Add("commit");
        });

        Assert.Equal(["lock", "agent", "commit"], order);
        Assert.Equal(2, rowsWhenTold);
        Assert.True(Assert.Single(world.KeyLock.Transactions).Committed);
    }

    /// <summary>A refused agent write rolls the add back, leaves one call, no compensation and a failure entry.</summary>
    [Fact]
    public async Task A_refused_agent_write_is_not_committed_and_is_not_compensated()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        world.Agent.SetAuthorizedKeysResult = Result.Fail(Error.Of("AgentInvalidInput", ErrorType.Validation));

        var run = await RunAsync(world, SftpTestKeys.P256, "second");

        Assert.False(run.Result.IsSuccess);
        Assert.Equal("AgentInvalidInput", run.Result.Error!.Code);
        var transaction = Assert.Single(world.KeyLock.Transactions);
        Assert.False(transaction.Committed);
        Assert.True(transaction.Disposed);
        Assert.Single(world.Agent.SetAuthorizedKeysCalls);
        Assert.Empty(world.Agent.EndSessionsCalls);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(SftpAuditActions.SftpKeyAdded, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A commit that fails after the agent accepted restores the previous set once and reports the failure.</summary>
    [Fact]
    public async Task A_commit_that_fails_after_the_agent_accepted_restores_the_previous_set_once()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        world.KeyLock.CommitFailure = new InvalidOperationException("connection lost");

        var run = await RunAsync(world, SftpTestKeys.P256, "second");

        Assert.False(run.Result.IsSuccess);
        Assert.Equal("SftpKeyChangeFailed", run.Result.Error!.Code);
        Assert.Equal(ErrorType.Failure, run.Result.Error.Type);
        Assert.Equal(2, world.Agent.SetAuthorizedKeysCalls.Count);
        Assert.Equal([SftpTestKeys.Ed25519, SftpTestKeys.P256], world.Agent.SetAuthorizedKeysCalls[0].PublicKeys);

        // The compensation is the set the login held BEFORE the request: the new key is gone again.
        var compensation = world.Agent.SetAuthorizedKeysCalls[1];
        Assert.Equal("alice", compensation.AccountUsername);
        Assert.Equal("deploy", compensation.SftpUsername);
        Assert.Equal([SftpTestKeys.Ed25519], compensation.PublicKeys);
        Assert.False(Assert.Single(world.KeyLock.Transactions).Committed);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A compensation that is itself refused is logged and the customer still gets the change-failed answer.</summary>
    [Fact]
    public async Task A_compensation_that_is_itself_refused_is_logged_and_the_answer_is_unchanged()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        world.KeyLock.CommitFailure = new InvalidOperationException("connection lost");
        var attempts = 0;
        world.Agent.OnSetAuthorizedKeys = () =>
        {
            attempts++;
            world.Agent.SetAuthorizedKeysResult = attempts == 1
                ? Result.Ok()
                : Result.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));
        };

        var run = await RunAsync(world, SftpTestKeys.P256, "second");

        Assert.Equal("SftpKeyChangeFailed", run.Result.Error!.Code);
        Assert.Equal(2, world.Agent.SetAuthorizedKeysCalls.Count);
        Assert.Contains(run.Logger.Messages, message =>
        {
            return message.Level == LogLevel.Error
                && message.Text.Contains("AgentSystemFailure", StringComparison.Ordinal);
        });
    }

    /// <summary>Another account's login answers not found, touches nothing and journals what was probed.</summary>
    [Fact]
    public async Task Another_accounts_login_answers_not_found_and_touches_nothing()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var run = await RunAsync(world, SftpTestKeys.P256, "intruder", loginId: world.StrangerLoginId);

        Assert.False(run.Result.IsSuccess);
        Assert.Equal("SftpUserNotFound", run.Result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, run.Result.Error.Type);
        Assert.Empty(world.Agent.SetAuthorizedKeysCalls);
        Assert.Empty(world.KeyLock.Locked);
        using var read = world.AdminContext();
        Assert.Equal(1, await read.SftpUserKeys.CountAsync(key => key.SftpUserId == world.StrangerLoginId));
        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(world.StrangerLoginId.ToString(), entry.Subject);
    }

    /// <summary>A login id that names nothing answers exactly as another tenant's does.</summary>
    [Fact]
    public async Task A_login_id_that_names_nothing_answers_exactly_as_another_tenants_does()
    {
        var world = await SftpKeyTestWorld.CreateAsync();

        var run = await RunAsync(world, SftpTestKeys.P256, "nowhere", loginId: Guid.NewGuid());

        Assert.Equal("SftpUserNotFound", run.Result.Error!.Code);
        Assert.Empty(world.Agent.SetAuthorizedKeysCalls);
    }

    /// <summary>A login whose account the directory does not know answers account-not-found and calls no agent.</summary>
    [Fact]
    public async Task A_login_whose_account_the_directory_does_not_know_calls_no_agent()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        var handler = NewHandler(
            world, new StubAccountDirectory(), new RecordingLogger<AddSftpUserKeyCommandHandler>());

        var result = await handler.HandleAsync(
            Command(world.OwnLoginId, SftpTestKeys.P256, "second"), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.SetAuthorizedKeysCalls);
        Assert.Empty(world.KeyLock.Locked);
    }

    /// <summary>A key the grammar refuses answers key-invalid, takes no lock and calls no agent.</summary>
    [Fact]
    public async Task A_key_the_grammar_refuses_takes_no_lock_and_calls_no_agent()
    {
        var run = await RunAsync("command=\"/bin/true\" " + SftpTestKeys.P256, "second");

        Assert.Equal("SftpKeyInvalid", run.Result.Error!.Code);
        Assert.Equal(ErrorType.Validation, run.Result.Error.Type);
        Assert.Empty(run.World.Agent.SetAuthorizedKeysCalls);
        Assert.Empty(run.World.KeyLock.Locked);
    }

    /// <summary>A label the entity refuses answers label-invalid, is not committed and calls no agent.</summary>
    [Fact]
    public async Task A_label_the_entity_refuses_is_not_committed_and_calls_no_agent()
    {
        var run = await RunAsync(SftpTestKeys.P256, "bad\nlabel");

        Assert.Equal("SftpKeyLabelInvalid", run.Result.Error!.Code);
        Assert.Empty(run.World.Agent.SetAuthorizedKeysCalls);
        Assert.False(Assert.Single(run.World.KeyLock.Transactions).Committed);
    }

    /// <summary>A key the login already holds answers a conflict and calls no agent.</summary>
    [Fact]
    public async Task A_key_the_login_already_holds_answers_a_conflict_and_calls_no_agent()
    {
        var run = await RunAsync(SftpTestKeys.Ed25519 + " renamed", "again");

        Assert.Equal("SftpKeyDuplicate", run.Result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, run.Result.Error.Type);
        Assert.Empty(run.World.Agent.SetAuthorizedKeysCalls);
        Assert.False(Assert.Single(run.World.KeyLock.Transactions).Committed);
    }

    /// <summary>The key a sibling login holds can be added to this login: a fingerprint is per login.</summary>
    [Fact]
    public async Task A_key_a_sibling_login_holds_can_be_added_to_this_login()
    {
        var run = await RunAsync(SftpTestKeys.P256, "shared");

        Assert.True(run.Result.IsSuccess);
    }

    /// <summary>The key after the maximum answers a conflict and calls no agent.</summary>
    [Fact]
    public async Task The_key_after_the_maximum_answers_a_conflict_and_calls_no_agent()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        await FillLoginAsync(world);

        var run = await RunAsync(world, SftpTestKeys.P256, "one too many");

        Assert.Equal("SftpKeyLimitReached", run.Result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, run.Result.Error.Type);
        Assert.Empty(world.Agent.SetAuthorizedKeysCalls);
    }

    /// <summary>Two simultaneous adds of one key leave the loser a conflict, never a server error.</summary>
    [Fact]
    public async Task A_unique_violation_at_save_is_a_conflict_and_calls_no_agent()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        var violation = new DbUpdateException(
            "save failed",
            new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));

        var run = await RunAsync(world, SftpTestKeys.P256, "second", saveFailure: violation);

        Assert.Equal("SftpKeyDuplicate", run.Result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, run.Result.Error.Type);
        Assert.Empty(world.Agent.SetAuthorizedKeysCalls);
        Assert.False(Assert.Single(world.KeyLock.Transactions).Committed);
    }

    /// <summary>A save failure that is not a unique violation is not swallowed as a conflict.</summary>
    [Fact]
    public async Task A_save_failure_that_is_not_a_unique_violation_is_not_reported_as_a_conflict()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        var failure = new DbUpdateException("save failed", new InvalidOperationException("other"));

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await RunAsync(world, SftpTestKeys.P256, "second", saveFailure: failure);
        });

        Assert.Empty(world.Agent.SetAuthorizedKeysCalls);
    }

    /// <summary>A success is journalled with the login, label and fingerprint, and never the key body or comment.</summary>
    [Fact]
    public async Task A_success_is_journalled_with_login_label_and_fingerprint_and_never_the_key_body()
    {
        var run = await RunAsync(SftpTestKeys.P256 + " " + SecretComment, "work laptop");

        var entry = Assert.Single(run.World.Audit.Entries);
        Assert.Equal(SftpAuditActions.SftpKeyAdded, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("deploy | work laptop | " + SftpTestKeys.P256Fingerprint, entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        AssertNoKeyMaterial(entry);
    }

    /// <summary>Every failure entry is free of the key body and the pasted comment as well.</summary>
    [Fact]
    public async Task Every_failure_entry_is_free_of_the_key_body_and_the_pasted_comment()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        world.Agent.SetAuthorizedKeysResult = Result.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        await RunAsync(world, SftpTestKeys.P256 + " " + SecretComment, "second");
        await RunAsync(world, "command=\"x\" " + SftpTestKeys.P384 + " " + SecretComment, "third");
        await RunAsync(world, SftpTestKeys.Ed25519 + " " + SecretComment, "duplicate");

        Assert.Equal(3, world.Audit.Entries.Count);
        Assert.All(world.Audit.Entries, entry =>
        {
            Assert.False(entry.Succeeded);
            AssertNoKeyMaterial(entry);
        });
    }

    /// <summary>The handler's log lines never carry the key body or the pasted comment.</summary>
    [Fact]
    public async Task The_handlers_log_lines_never_carry_the_key_body_or_the_pasted_comment()
    {
        var world = await SftpKeyTestWorld.CreateAsync();
        world.KeyLock.CommitFailure = new InvalidOperationException("failed while writing " + SftpTestKeys.P256);

        var run = await RunAsync(world, SftpTestKeys.P256 + " " + SecretComment, "second");

        Assert.NotEmpty(run.Logger.Messages);
        Assert.All(run.Logger.Messages, message =>
        {
            Assert.DoesNotContain(SftpTestKeys.P256.Split(' ')[1], message.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretComment, message.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("failed while writing", message.Text, StringComparison.Ordinal);
        });
    }

    /// <summary>The key-material check fails on a body it is looking for, so the assertions above can go red.</summary>
    [Fact]
    public void The_key_material_check_fails_on_an_entry_that_carries_a_key_body()
    {
        var leaking = new AuditEntry(
            Guid.NewGuid(),
            "tester",
            "SftpKeyAdded",
            "deploy | " + SftpTestKeys.P256.Split(' ')[1],
            "203.0.113.7",
            "tests",
            true);

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
        {
            AssertNoKeyMaterial(leaking);
        });
    }

    /// <summary>Runs one add against a fresh world, adding to the caller's own login.</summary>
    /// <param name="publicKey">The pasted key text.</param>
    /// <param name="label">The label.</param>
    private static async Task<Run> RunAsync(string publicKey, string label)
    {
        return await RunAsync(await SftpKeyTestWorld.CreateAsync(), publicKey, label);
    }

    /// <summary>Runs one add against <paramref name="world"/>.</summary>
    /// <param name="world">The world to run in.</param>
    /// <param name="publicKey">The pasted key text.</param>
    /// <param name="label">The label.</param>
    /// <param name="loginId">The login to add to; the caller's own <c>deploy</c> when omitted.</param>
    /// <param name="saveFailure">When given, the exception the handler's save throws.</param>
    /// <param name="transactionOnCommit">Runs when the transaction is asked to commit.</param>
    private static async Task<Run> RunAsync(
        SftpKeyTestWorld world,
        string publicKey,
        string label,
        Guid? loginId = null,
        Exception? saveFailure = null,
        Action? transactionOnCommit = null)
    {
        var logger = new RecordingLogger<AddSftpUserKeyCommandHandler>();
        var handler = NewHandler(world, world.Accounts, logger, saveFailure);
        if (transactionOnCommit is not null)
        {
            world.KeyLock.OnBegin += () =>
            {
                world.KeyLock.Transactions[^1].OnCommit = transactionOnCommit;
            };
        }

        var result = await handler.HandleAsync(
            Command(loginId ?? world.OwnLoginId, publicKey, label), CancellationToken.None);

        return new Run(world, result, logger);
    }

    /// <summary>Builds the handler over the world's doubles.</summary>
    /// <param name="world">The world.</param>
    /// <param name="accounts">The account directory to use.</param>
    /// <param name="logger">The logger to use.</param>
    /// <param name="saveFailure">When given, the exception the handler's save throws.</param>
    private static AddSftpUserKeyCommandHandler NewHandler(
        SftpKeyTestWorld world,
        StubAccountDirectory accounts,
        RecordingLogger<AddSftpUserKeyCommandHandler> logger,
        Exception? saveFailure = null)
    {
        return new AddSftpUserKeyCommandHandler(
            world.CallerContext(saveFailure),
            accounts,
            world.Agent,
            world.KeyLock,
            world.Journal,
            new FakeClock(Now),
            logger);
    }

    /// <summary>Builds the command with the usual audit context.</summary>
    /// <param name="loginId">The login the key is for.</param>
    /// <param name="publicKey">The pasted key text.</param>
    /// <param name="label">The label.</param>
    private static AddSftpUserKeyCommand Command(Guid loginId, string publicKey, string label)
    {
        return new AddSftpUserKeyCommand(label, publicKey, loginId, "203.0.113.7", "tests");
    }

    /// <summary>Fills the caller's <c>deploy</c> login to the maximum number of keys.</summary>
    /// <param name="world">The world.</param>
    private static async Task FillLoginAsync(SftpKeyTestWorld world)
    {
        using var context = world.AdminContext();
        var login = await context.SftpUsers.Include(row => row.Keys).SingleAsync(row => row.Id == world.OwnLoginId);
        for (var index = 0; login.Keys.Count < SftpKeyLimits.MaxPerLogin; index++)
        {
            login.AddKey(
                Guid.NewGuid(),
                $"filler {index}",
                SftpPublicKey.Parse(SftpTestKeys.Distinct(index)).Value,
                SftpKeyTestWorld.Epoch);
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Asserts that no field of an audit entry carries a key body or the pasted comment.</summary>
    /// <param name="entry">The entry.</param>
    private static void AssertNoKeyMaterial(AuditEntry entry)
    {
        var text = string.Join(" ", entry.Action, entry.Subject, entry.ActorUsername, entry.IpAddress, entry.UserAgent);

        foreach (var line in new[] { SftpTestKeys.Ed25519, SftpTestKeys.P256, SftpTestKeys.P384 })
        {
            Assert.DoesNotContain(line.Split(' ')[1], text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(SecretComment, text, StringComparison.Ordinal);
    }

    /// <summary>The outcome of one add and what it ran against.</summary>
    /// <param name="World">The world the add ran in.</param>
    /// <param name="Result">What the handler answered.</param>
    /// <param name="Logger">What the handler logged.</param>
    private sealed record Run(
        SftpKeyTestWorld World,
        Result<SftpUserKeyDto> Result,
        RecordingLogger<AddSftpUserKeyCommandHandler> Logger);
}
