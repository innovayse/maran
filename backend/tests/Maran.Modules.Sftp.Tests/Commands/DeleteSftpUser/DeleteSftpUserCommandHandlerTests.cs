using Maran.Modules.Sftp.Commands.DeleteSftpUser;
using Maran.Modules.Sftp.Services;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sftp.Tests.Commands.DeleteSftpUser;

/// <summary>Which login a delete removes, in what order, and whose it may be.</summary>
public sealed class DeleteSftpUserCommandHandlerTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Deleting another tenants sftp user answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Deleting_another_tenants_sftp_user_answers_not_found_rather_than_forbidden()
    {
        var world = await WorldAsync();

        var result = await world.DeleteAsync(world.StrangerSftpUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNotFound", result.Error!.Code);

        // And the host was never touched: a successful delete here would revoke another customer's
        // only way of reaching their own files.
        Assert.Empty(world.Agent.Deletes);
    }

    /// <summary>A delete names the suffix the row recorded and never the fully qualified login.</summary>
    [Fact]
    public async Task A_delete_names_the_suffix_the_row_recorded_and_never_the_fully_qualified_login()
    {
        // The agent applies the account prefix itself, so a delete cannot reach past this account
        // even if the row it read were wrong.
        var world = await WorldAsync();

        var result = await world.DeleteAsync(world.OwnSftpUserId);

        Assert.True(result.IsSuccess);
        var call = Assert.Single(world.Agent.Deletes);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal("deploy", call.SftpUsername);
    }

    /// <summary>Deleting an sftp user removes the row only after the agent confirms.</summary>
    [Fact]
    public async Task Deleting_an_sftp_user_removes_the_row_only_after_the_agent_confirms()
    {
        // Agent first, row second. A row removed while the login still exists is a live credential
        // into a customer's home that nobody in the panel can see and nobody can now revoke — and
        // the customer asked for exactly that access to end.
        var world = await WorldAsync();
        world.Agent.DeleteResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.DeleteAsync(world.OwnSftpUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);

        using var read = SftpTestContext.Create(FakeCurrentUser.Admin(), world.Store);
        Assert.Equal(2, await read.SftpUsers.CountAsync());
    }

    /// <summary>A successful delete removes the row as well as the login.</summary>
    [Fact]
    public async Task A_successful_delete_removes_the_row_as_well_as_the_login()
    {
        var world = await WorldAsync();

        await world.DeleteAsync(world.OwnSftpUserId);

        using var read = SftpTestContext.Create(FakeCurrentUser.Admin(), world.Store);
        Assert.Equal([world.StrangerSftpUserId], await read.SftpUsers.Select(row => row.Id).ToListAsync());
    }

    /// <summary>A delete is journalled with the login name.</summary>
    [Fact]
    public async Task A_delete_is_journalled_with_the_login_name()
    {
        var world = await WorldAsync();

        await world.DeleteAsync(world.OwnSftpUserId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.SftpUserDeleted, entry.Action);
        Assert.Equal("deploy", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A refused delete is journalled as a failure naming what was probed for.</summary>
    [Fact]
    public async Task A_refused_delete_is_journalled_as_a_failure_naming_what_was_probed_for()
    {
        var world = await WorldAsync();

        await world.DeleteAsync(world.StrangerSftpUserId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.SftpUserDeleted, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(world.StrangerSftpUserId.ToString(), entry.Subject);
    }

    /// <summary>An identifier that names nothing answers exactly as another tenants does.</summary>
    [Fact]
    public async Task An_identifier_that_names_nothing_answers_exactly_as_another_tenants_does()
    {
        var world = await WorldAsync();

        var result = await world.DeleteAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNotFound", result.Error!.Code);
    }

    /// <summary>Builds a world holding one login for each of two accounts.</summary>
    private static async Task<World> WorldAsync()
    {
        var store = Guid.NewGuid().ToString();

        using var seed = SftpTestContext.Create(FakeCurrentUser.Admin(), store);
        var own = SftpTestContext.Row(OwnerAccountId, "alice", "deploy");
        var stranger = SftpTestContext.Row(StrangerAccountId, "bob", "backup");
        seed.SftpUsers.AddRange(own, stranger);
        await seed.SaveChangesAsync();

        return new World(store, own.Id, stranger.Id);
    }

    /// <summary>Everything one delete test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The shared in-memory store both principals read.</summary>
        public string Store { get; }

        /// <summary>The signed-in customer's own login.</summary>
        public Guid OwnSftpUserId { get; }

        /// <summary>The other tenant's login, which must answer 404.</summary>
        public Guid StrangerSftpUserId { get; }

        /// <summary>The agent double every call is recorded on.</summary>
        public RecordingAgentSftpClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The handler under test.</summary>
        public DeleteSftpUserCommandHandler Handler { get; }

        /// <summary>Wires one world.</summary>
        /// <param name="store">The shared in-memory store.</param>
        /// <param name="ownSftpUserId">The signed-in customer's login.</param>
        /// <param name="strangerSftpUserId">The other tenant's login.</param>
        public World(string store, Guid ownSftpUserId, Guid strangerSftpUserId)
        {
            Store = store;
            OwnSftpUserId = ownSftpUserId;
            StrangerSftpUserId = strangerSftpUserId;

            var currentUser = FakeCurrentUser.Customer(OwnerAccountId);
            Handler = new DeleteSftpUserCommandHandler(
                SftpTestContext.Create(currentUser, store),
                new StubAccountDirectory(new AccountSnapshot(OwnerAccountId, "alice", 5, 5, 5, 5, 5, 1_024)),
                Agent,
                new SftpAuditJournal(Audit, currentUser));
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="sftpUserId">Which login to remove.</param>
        public async Task<Result<bool>> DeleteAsync(Guid sftpUserId)
        {
            return await Handler.HandleAsync(
                new DeleteSftpUserCommand(sftpUserId, "203.0.113.7", "tests"),
                CancellationToken.None);
        }
    }
}
