using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// The one place a destination is chosen, and the second of the two boundaries that refuse a remote
/// one.
/// </summary>
public sealed class BackupDestinationResolverTests
{
    /// <summary>A request naming no destination resolves this servers default one.</summary>
    /// <remarks>
    /// Which is what a historic <c>Backup.DestinationId</c> of null already meant, so a row written
    /// before the destinations table existed still names the place its artifact is.
    /// </remarks>
    [Fact]
    public async Task Naming_no_destination_resolves_the_servers_default_one()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());
        var resolver = new BackupDestinationResolver(dbContext);

        var resolved = await resolver.ResolveAsync(null, CancellationToken.None);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(BackupDestination.DefaultDestinationId, resolved.Value!.Id);
        Assert.Equal(AgentBackupDestinationKind.Local, resolved.Value.Agent.Kind);
        Assert.Equal(string.Empty, resolved.Value.Agent.Path);
    }

    /// <summary>A server with no default destination refuses rather than inventing one.</summary>
    /// <remarks>
    /// The alternative — rebuilding a destination from configuration — would write backups against a
    /// destination the panel has no record of, invisible to retention and stamping a null onto rows
    /// that were supposed to stop carrying one.
    /// </remarks>
    [Fact]
    public async Task A_server_with_no_default_destination_refuses_by_name()
    {
        await using var dbContext = BackupsTestContext.Create(FakeCurrentUser.Admin());
        var resolver = new BackupDestinationResolver(dbContext);

        var resolved = await resolver.ResolveAsync(null, CancellationToken.None);

        Assert.False(resolved.IsSuccess);
        Assert.Equal("BackupDestinationNotConfigured", resolved.Error!.Code);
        Assert.Equal(ErrorType.Failure, resolved.Error.Type);
    }

    /// <summary>A destination that is not there is a not-found, not a fall back to the default.</summary>
    [Fact]
    public async Task A_destination_that_does_not_exist_is_not_found()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());
        var resolver = new BackupDestinationResolver(dbContext);

        var resolved = await resolver.ResolveAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(resolved.IsSuccess);
        Assert.Equal("BackupDestinationNotFound", resolved.Error!.Code);
        Assert.Equal(ErrorType.NotFound, resolved.Error.Type);
    }

    /// <summary>A stored remote destination is refused before it becomes an argument to the agent.</summary>
    /// <remarks>
    /// No endpoint can store one, so this row can only have arrived another way — a hand edit, or a
    /// later migration. The database holding what a validator refuses is a state this module already
    /// reasons about, and the refusal has to sit in front of the call to a root process rather than
    /// after it.
    /// </remarks>
    [Fact]
    public async Task A_stored_remote_destination_is_refused()
    {
        await using var dbContext = BackupsTestContext.Create(FakeCurrentUser.Admin());
        var remote = new BackupDestination(
            Guid.NewGuid(),
            "Somebody's bucket",
            BackupDestinationKind.S3,
            string.Empty,
            isDefault: true,
            BackupsTestContext.SeedInstant);
        dbContext.BackupDestinations.Add(remote);
        await dbContext.SaveChangesAsync();

        var resolved = await new BackupDestinationResolver(dbContext).ResolveAsync(null, CancellationToken.None);

        Assert.False(resolved.IsSuccess);
        Assert.Equal("BackupDestinationRemoteUnsupported", resolved.Error!.Code);
    }
}
