using System.Globalization;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Queries.ListBackupDestinations;
using Maran.Modules.Backups.Tests.TestSupport;

namespace Maran.Modules.Backups.Tests.Queries.ListBackupDestinations;

/// <summary>The listing a settings screen reads, and where the path on it comes from.</summary>
/// <remarks>
/// Both directions are pinned, because a listing that never shows a path passes the "it does not
/// invent one" test just as well as a correct one (rules/testing.md): an agent that states its
/// backup directory produces that exact directory on the local destination, and an agent that states
/// nothing produces <c>null</c> — never the literal the panel could have guessed.
/// </remarks>
public sealed class ListBackupDestinationsQueryHandlerTests
{
    /// <summary>The default destination is answered first, showing the directory the agent stated.</summary>
    [Fact]
    public async Task The_default_destination_is_answered_first_with_the_agents_backup_root()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());
        dbContext.BackupDestinations.Add(new BackupDestination(
            Guid.NewGuid(),
            "Another",
            BackupDestinationKind.Local,
            string.Empty,
            isDefault: false,
            BackupsTestContext.SeedInstant));
        await dbContext.SaveChangesAsync();

        var listed = await Handler(dbContext, StubAgentSystemClient.Reporting(BackupsTestContext.AgentBackupRoot))
            .HandleAsync(new ListBackupDestinationsQuery(), CancellationToken.None);

        Assert.Equal(2, listed.Count);
        Assert.True(listed[0].IsDefault);
        Assert.Equal(BackupsTestContext.AgentBackupRoot, listed[0].Path);
    }

    /// <summary>A directory the agent states is shown as it stated it, not as the panel expected it.</summary>
    /// <remarks>
    /// The agent is the only statement of this path, so a listing that agreed with the agent only
    /// when the agent said the familiar literal would be restating a constant and calling it a read.
    /// </remarks>
    [Fact]
    public async Task The_path_shown_is_whatever_the_agent_stated()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());

        var listed = await Handler(dbContext, StubAgentSystemClient.Reporting("/mnt/volume/maran"))
            .HandleAsync(new ListBackupDestinationsQuery(), CancellationToken.None);

        Assert.Equal("/mnt/volume/maran", Assert.Single(listed).Path);
    }

    /// <summary>An unreachable agent leaves the path unknown rather than filled with a default.</summary>
    [Fact]
    public async Task An_unreachable_agent_leaves_the_path_unestablished()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());

        var listed = await Handler(dbContext, StubAgentSystemClient.Unreachable())
            .HandleAsync(new ListBackupDestinationsQuery(), CancellationToken.None);

        Assert.Null(Assert.Single(listed).Path);
    }

    /// <summary>An agent older than the field leaves the path unknown too.</summary>
    /// <remarks>
    /// Empty is proto3's "this agent predates the field", and it is the same answer to the only
    /// question the screen asks — does the panel know the path? — as no answer at all.
    /// </remarks>
    [Fact]
    public async Task An_agent_that_states_no_backup_root_leaves_the_path_unestablished()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());

        var listed = await Handler(dbContext, StubAgentSystemClient.Reporting(string.Empty))
            .HandleAsync(new ListBackupDestinationsQuery(), CancellationToken.None);

        Assert.Null(Assert.Single(listed).Path);
    }

    /// <summary>A server that has not been reconciled answers an empty list rather than throwing.</summary>
    [Fact]
    public async Task A_server_with_no_destination_answers_an_empty_list()
    {
        await using var dbContext = BackupsTestContext.Create(FakeCurrentUser.Admin());

        var listed = await Handler(dbContext, StubAgentSystemClient.Unreachable())
            .HandleAsync(new ListBackupDestinationsQuery(), CancellationToken.None);

        Assert.Empty(listed);
    }

    /// <summary>Builds the handler over a context and a scripted agent.</summary>
    /// <param name="dbContext">The context to read from.</param>
    /// <param name="agent">The handshake double.</param>
    /// <returns>The handler under test.</returns>
    private static ListBackupDestinationsQueryHandler Handler(
        Maran.Modules.Backups.Persistence.BackupsDbContext dbContext,
        StubAgentSystemClient agent)
    {
        return new ListBackupDestinationsQueryHandler(dbContext, agent, BackupsTestContext.DestinationNames());
    }

    /// <summary>The panels own destination is listed with a display name beside its stored name.</summary>
    /// <remarks>
    /// The pinned defect, measured in a browser: the screen headed its only card "Local storage"
    /// while every other word on the page was Russian. Both values are asserted, because the fix is
    /// that there are now two: the row keeps its English name — which is what an operator reads in
    /// <c>psql</c> and what a support ticket names — and the screen is given something else to show.
    /// </remarks>
    [Fact]
    public async Task The_panels_own_destination_is_listed_with_a_display_name_beside_its_stored_name()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());

        var listed = await Handler(dbContext, StubAgentSystemClient.Reporting(BackupsTestContext.AgentBackupRoot))
            .HandleAsync(new ListBackupDestinationsQuery(), CancellationToken.None);

        var destination = Assert.Single(listed);
        Assert.Equal("Local storage", destination.Name);
        Assert.Equal("Local storage", destination.DisplayName);
    }

    /// <summary>A destination an operator named is listed under the operators own words.</summary>
    /// <remarks>
    /// The branch that keeps the fix from becoming a translator of other people's text, and it is
    /// run in RUSSIAN on purpose: the seeded row would be renamed under this culture, so a rewrite
    /// keying on the stored name rather than on the seeded row's identity fails here while passing
    /// the test above.
    /// </remarks>
    [Fact]
    public async Task A_destination_an_operator_named_is_listed_under_the_operators_own_words()
    {
        await using var dbContext = BackupsTestContext.Create(FakeCurrentUser.Admin());
        dbContext.BackupDestinations.Add(new BackupDestination(
            Guid.NewGuid(),
            "Local storage",
            BackupDestinationKind.Local,
            string.Empty,
            isDefault: true,
            BackupsTestContext.SeedInstant));
        await dbContext.SaveChangesAsync();

        var previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru");

            var listed = await Handler(dbContext, StubAgentSystemClient.Reporting(BackupsTestContext.AgentBackupRoot))
                .HandleAsync(new ListBackupDestinationsQuery(), CancellationToken.None);

            var destination = Assert.Single(listed);
            Assert.Equal("Local storage", destination.Name);
            Assert.Equal("Local storage", destination.DisplayName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
