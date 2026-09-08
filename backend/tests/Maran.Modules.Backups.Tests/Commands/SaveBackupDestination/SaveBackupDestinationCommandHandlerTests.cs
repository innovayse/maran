using Maran.Modules.Backups.Commands.SaveBackupDestination;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Commands.SaveBackupDestination;

/// <summary>
/// The earlier of the two boundaries that refuse a destination this build cannot act on: the one
/// that stops a remote row ever being stored.
/// </summary>
public sealed class SaveBackupDestinationCommandHandlerTests
{
    /// <summary>An S3 destination is refused with the code a screen branches on.</summary>
    /// <remarks>
    /// Validation and not a server failure: the caller asked for something they could have asked
    /// differently, and the answer says what this build does rather than that they were wrong about
    /// the shape of their request.
    /// </remarks>
    [Fact]
    public async Task An_s3_destination_is_refused_as_unsupported()
    {
        var world = World.New();

        var result = await world.Handler.HandleAsync(
            Command(BackupDestinationKind.S3), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupDestinationRemoteUnsupported", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>A second local destination is refused, because the agent writes to one root.</summary>
    [Fact]
    public async Task A_second_local_destination_is_refused_as_already_defined()
    {
        var world = World.New();

        var result = await world.Handler.HandleAsync(
            Command(BackupDestinationKind.Local), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupDestinationLocalAlreadyDefined", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    /// <summary>The attempt is audited whichever way it was refused.</summary>
    /// <remarks>
    /// The line is the record of the storage an operator asked for and did not get, which is the
    /// evidence the question of whether to build the remote arm should be answered from.
    /// </remarks>
    [Theory]
    [InlineData(BackupDestinationKind.Local)]
    [InlineData(BackupDestinationKind.S3)]
    public async Task The_refused_attempt_is_audited_with_the_callers_address(BackupDestinationKind kind)
    {
        var world = World.New();

        await world.Handler.HandleAsync(Command(kind), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupDestinationSaved, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Equal(CallerAddress, entry.IpAddress);
    }

    /// <summary>The address the audit entries are asserted against.</summary>
    private const string CallerAddress = "10.0.0.1";

    /// <summary>Builds the command under test.</summary>
    /// <param name="kind">The destination kind the operator asked for.</param>
    /// <returns>A named destination of that kind, stamped with a caller.</returns>
    private static SaveBackupDestinationCommand Command(BackupDestinationKind kind)
    {
        return new SaveBackupDestinationCommand("Off-site", kind, CallerAddress, "agent");
    }

    /// <summary>The doubles and the handler, assembled once per test.</summary>
    private sealed class World
    {
        /// <summary>The handler under test.</summary>
        public SaveBackupDestinationCommandHandler Handler { get; }

        /// <summary>The journal double, holding what was recorded.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>Assembles the handler over doubles.</summary>
        private World()
        {
            var currentUser = FakeCurrentUser.Admin();
            Audit = new RecordingAuditWriter();
            Handler = new SaveBackupDestinationCommandHandler(new BackupAuditJournal(Audit, currentUser));
        }

        /// <summary>Assembles a world.</summary>
        /// <returns>The assembled world.</returns>
        public static World New()
        {
            return new World();
        }
    }
}
