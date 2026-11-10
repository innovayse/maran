using System.Text.RegularExpressions;
using Maran.Modules.Identity.Commands.RemoveSubUser;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Commands.RemoveSubUser;

/// <summary>
/// What removing a sub-user does to the audit journal: a login that is deleted is pseudonymised in the
/// same transaction, a login that stays is a person who still exists and is left alone.
/// </summary>
public sealed class RemoveSubUserCommandHandlerErasureTests : IDisposable
{
    /// <summary>The address of the sub-user's own rows, distinct from every other row's.</summary>
    private const string MemberAddress = "198.51.100.88";

    /// <summary>The user agent of the sub-user's own rows, distinct from every other row's.</summary>
    private const string MemberAgent = "deploy-browser/1";

    /// <summary>The sub-user's e-mail address, as the world's seeding builds it.</summary>
    private const string MemberEmail = "deploy@example.com";

    /// <summary>The world every test starts from: an account, its owner, a clock and a recording journal.</summary>
    private readonly SubUserWorld _world = new();

    /// <summary>Releases the database.</summary>
    public void Dispose()
    {
        _world.Dispose();
    }

    /// <summary>
    /// Removing a team-only login leaves no original identifier of it in the journal, including the owner's
    /// invitation row that named it; the search first finds the originals in the seeded rows.
    /// </summary>
    [Fact]
    public async Task Removing_a_team_only_login_leaves_no_original_identifier_of_it_in_the_journal()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);
        var needles = new[] { member.Id.ToString("D"), "deploy", MemberEmail, MemberAddress, MemberAgent };
        await SeedJournalAsync(member);
        Assert.True(ErasureJournal.Mentions(await ErasureJournal.AllAsync(_world.Context), needles));

        var result = await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rows = await ErasureJournal.AllAsync(_world.Context);
        Assert.False(ErasureJournal.Mentions(rows, needles));
        Assert.Equal(3, rows.Count);
    }

    /// <summary>The owner's own rows keep their actor: the owner is a person who still exists.</summary>
    [Fact]
    public async Task Removing_a_login_keeps_the_owners_own_columns()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);
        await SeedJournalAsync(member);

        await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        var owned = (await ErasureJournal.AllAsync(_world.Context))
            .Where(row => { return row.ActorUserId == _world.Owner.Id; })
            .ToList();
        Assert.Equal(2, owned.Count);
        Assert.All(owned, row =>
        {
            Assert.Equal("acme", row.ActorUsername);
            Assert.Equal("203.0.113.7", row.IpAddress);
        });
    }

    /// <summary>
    /// The removal is journalled under the same pseudonym as the person's earlier rows, followed by one
    /// summary; neither names the login.
    /// </summary>
    [Fact]
    public async Task The_removal_and_its_summary_name_the_login_by_its_pseudonym()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);
        await SeedJournalAsync(member);

        await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        Assert.Equal(2, _world.Audit.Written.Count);
        var removal = _world.Audit.Written[0];
        var summary = _world.Audit.Written[1];
        Assert.Equal(IdentityAuditActions.SubUserRemoved, removal.Action);
        Assert.Matches(new Regex("^erased-user-[0-9a-f]{8}$"), removal.Subject);
        Assert.Equal(IdentityAuditActions.PersonalDataErased, summary.Action);
        Assert.Equal("maran-identity", summary.ActorUsername);
        Assert.Equal(removal.Subject + " rows=2", summary.Subject);
    }

    /// <summary>A login that keeps another account stays whole: nothing is pseudonymised, and the removal names it as before.</summary>
    [Fact]
    public async Task Removing_a_login_with_other_access_pseudonymises_nothing()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);
        _world.Context.AccountMemberships.Add(
            AccountMembership.Admit(Guid.NewGuid(), member.Id, Guid.NewGuid(), AccountFeature.Cron, SubUserWorld.Now));
        await _world.Context.SaveChangesAsync();
        await SeedJournalAsync(member);
        var before = (await ErasureJournal.AllAsync(_world.Context)).Select(ErasureJournal.TextOf).Order().ToList();

        await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        var after = await ErasureJournal.AllAsync(_world.Context);
        Assert.Equal(before, after.Select(ErasureJournal.TextOf).Order().ToList());
        Assert.All(after, row => { Assert.Null(row.ErasedAt); });
        var removal = Assert.Single(_world.Audit.Written);
        Assert.Equal(IdentityAuditActions.SubUserRemoved, removal.Action);
        Assert.Equal("deploy", removal.Subject);
    }

    /// <summary>A login that never appeared in the journal is deleted without a summary: there was nothing to erase.</summary>
    [Fact]
    public async Task Removing_a_login_the_journal_never_named_writes_no_summary()
    {
        var member = await _world.SeedMemberAsync("deploy", AccountFeature.Databases);

        await NewHandler().HandleAsync(Command(member.Id), CancellationToken.None);

        var removal = Assert.Single(_world.Audit.Written);
        Assert.Equal(IdentityAuditActions.SubUserRemoved, removal.Action);
    }

    /// <summary>Builds the handler over the world, acting as the owner.</summary>
    /// <returns>The handler under test.</returns>
    private RemoveSubUserCommandHandler NewHandler()
    {
        return new RemoveSubUserCommandHandler(
            _world.Context,
            _world.Guard(_world.Caller),
            new IdentityAuditJournal(_world.Audit, _world.Caller),
            ErasureHandlers.Pseudonymiser(_world.Context, _world.Audit, _world.Caller),
            _world.Clock,
            _world.Caller);
    }

    /// <summary>Builds the request to remove <paramref name="userId"/>.</summary>
    /// <param name="userId">The sub-user to remove.</param>
    /// <returns>The command.</returns>
    private static RemoveSubUserCommand Command(Guid userId)
    {
        return new RemoveSubUserCommand(userId, "203.0.113.7", "agent");
    }

    /// <summary>Stores three journal rows: the sub-user's own, the owner's invitation of them, and an unrelated owner row.</summary>
    /// <param name="member">The sub-user the rows are about.</param>
    /// <returns>Resolves once the rows are stored.</returns>
    private async Task SeedJournalAsync(User member)
    {
        _world.Context.AuditEvents.AddRange(
            ErasureJournal.Row(member.Id, "deploy", "own-action", MemberAddress, MemberAgent),
            ErasureJournal.Row(
                _world.Owner.Id,
                "acme",
                "deploy",
                action: IdentityAuditActions.SubUserInvited,
                subjectUserId: member.Id),
            ErasureJournal.Row(_world.Owner.Id, "acme", "unrelated"));
        await _world.Context.SaveChangesAsync();
    }
}
