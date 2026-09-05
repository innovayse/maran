using Maran.Modules.Sftp.Commands.ResetSftpUserPassword;
using Maran.Modules.Sftp.Common;
using Maran.Modules.Sftp.Services;
using Maran.Modules.Sftp.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sftp.Tests.Commands.ResetSftpUserPassword;

/// <summary>
/// The only recovery there is for a lost SFTP password: what it changes, who may ask for it, and
/// what it refuses to keep.
/// </summary>
public sealed class ResetSftpUserPasswordCommandHandlerTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Resetting a password returns a new one once and stores no copy.</summary>
    [Fact]
    public async Task Resetting_a_password_returns_a_new_one_once_and_stores_no_copy()
    {
        var world = await WorldAsync();

        var result = await world.ResetAsync(world.OwnSftpUserId);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.Password.Reveal());

        // The value the agent installed is the value the customer is shown, or the customer has a
        // password that does not work.
        var call = Assert.Single(world.Agent.PasswordChanges);
        Assert.Equal(call.Password.Reveal(), result.Value.Password.Reveal());

        // And nothing in the row holds it, over every mapped property rather than a remembered list.
        using var read = SftpTestContext.Create(FakeCurrentUser.Admin(), world.Store);
        var row = await read.SftpUsers.SingleAsync(sftpUser => sftpUser.Id == world.OwnSftpUserId);
        foreach (var property in read.Entry(row).Properties)
        {
            Assert.NotEqual(result.Value.Password.Reveal(), property.CurrentValue as string);
        }
    }

    /// <summary>Resetting another tenants sftp password answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Resetting_another_tenants_sftp_password_answers_not_found_rather_than_forbidden()
    {
        // The module's most valuable IDOR. Every other cross-tenant read merely discloses that a
        // login exists; this one, if it could be pointed at somebody else's row, would hand the
        // caller a WORKING CREDENTIAL into their home directory. And 403 would confirm the row is
        // real.
        var world = await WorldAsync();

        var result = await world.ResetAsync(world.StrangerSftpUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNotFound", result.Error!.Code);
        Assert.Empty(world.Agent.PasswordChanges);
    }

    /// <summary>An identifier that names nothing answers exactly as another tenants does.</summary>
    [Fact]
    public async Task An_identifier_that_names_nothing_answers_exactly_as_another_tenants_does()
    {
        var world = await WorldAsync();

        var result = await world.ResetAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Equal("SftpUserNotFound", result.Error!.Code);
    }

    /// <summary>The agent is addressed by the name suffix the row recorded.</summary>
    [Fact]
    public async Task The_agent_is_addressed_by_the_name_suffix_the_row_recorded()
    {
        // The suffix, never the fully-qualified login: the agent applies the prefix itself, so a
        // reset cannot express another tenant's login even if the row it read were wrong.
        var world = await WorldAsync();

        await world.ResetAsync(world.OwnSftpUserId);

        var call = Assert.Single(world.Agent.PasswordChanges);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal("deploy", call.SftpUsername);
    }

    /// <summary>The answer names the login the customer actually signs in with.</summary>
    [Fact]
    public async Task The_answer_names_the_login_the_customer_actually_signs_in_with()
    {
        // The response carries the FULL name, not the suffix: a new password beside a user name the
        // host does not know is a credential the customer cannot use.
        var world = await WorldAsync();

        var result = await world.ResetAsync(world.OwnSftpUserId);

        Assert.Equal("alice_deploy", result.Value.FullName);
    }

    /// <summary>An agent that refuses leaves the old password live and is reported as it answered.</summary>
    [Fact]
    public async Task An_agent_that_refuses_leaves_the_old_password_live_and_is_reported_as_it_answered()
    {
        var world = await WorldAsync();
        world.Agent.SetPasswordResult = Result<bool>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await world.ResetAsync(world.OwnSftpUserId);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentNotFound", result.Error!.Code);
    }

    /// <summary>A reset is journalled with the login name and never with the new password.</summary>
    [Fact]
    public async Task A_reset_is_journalled_with_the_login_name_and_never_with_the_new_password()
    {
        // The journal is never deleted, so an entry naming the value would be the copy this module
        // takes such trouble not to keep.
        var world = await WorldAsync();

        var result = await world.ResetAsync(world.OwnSftpUserId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.SftpUserPasswordReset, entry.Action);
        Assert.Equal("deploy", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain(result.Value.Password.Reveal(), entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A refused reset is journalled as a failure naming what was probed for.</summary>
    [Fact]
    public async Task A_refused_reset_is_journalled_as_a_failure_naming_what_was_probed_for()
    {
        var world = await WorldAsync();

        await world.ResetAsync(world.StrangerSftpUserId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.SftpUserPasswordReset, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(world.StrangerSftpUserId.ToString(), entry.Subject);
    }

    /// <summary>The new password is drawn only from the alphabet the agent accepts.</summary>
    [Fact]
    public async Task The_new_password_is_drawn_only_from_the_alphabet_the_agent_accepts()
    {
        var world = await WorldAsync();

        var result = await world.ResetAsync(world.OwnSftpUserId);

        Assert.All(
            result.Value.Password.Reveal(),
            character =>
            {
                Assert.True(
                    ProvisionedPasswordGenerator.Alphabet.Contains(character, StringComparison.Ordinal),
                    $"the generator produced {character}, which the agent's Password type would refuse");
            });
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

    /// <summary>Everything one reset test needs, wired the way the Host wires it.</summary>
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
        public ResetSftpUserPasswordCommandHandler Handler { get; }

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
            Handler = new ResetSftpUserPasswordCommandHandler(
                SftpTestContext.Create(currentUser, store),
                new StubAccountDirectory(new AccountSnapshot(OwnerAccountId, "alice", 5, 5, 5, 5, 5, 1_024)),
                Agent,
                new SftpAuditJournal(Audit, currentUser));
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="sftpUserId">Which login to re-credential.</param>
        public async Task<Result<SftpUserPasswordDto>> ResetAsync(Guid sftpUserId)
        {
            return await Handler.HandleAsync(
                new ResetSftpUserPasswordCommand(sftpUserId, "203.0.113.7", "tests"),
                CancellationToken.None);
        }
    }
}
