using Maran.Modules.Backups.Commands.SetBackupDestinationEncryption;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Commands.SetBackupDestinationEncryption;

/// <summary>
/// Switching a destination's archive encryption on and off: the key is made once, kept, never
/// returned, and every attempt is audited without naming it.
/// </summary>
public sealed class SetBackupDestinationEncryptionCommandHandlerTests
{
    /// <summary>The address the audit entries are asserted against.</summary>
    private const string CallerAddress = "10.0.0.1";

    /// <summary>Switching it on makes the destination's key and encrypts from then on.</summary>
    [Fact]
    public async Task Enabling_mints_the_destinations_key_and_turns_encryption_on()
    {
        await using var world = World.New();

        var result = await world.HandleAsync(enabled: true);

        Assert.True(result.IsSuccess);
        var destination = await world.DestinationAsync();
        Assert.True(destination.EncryptArchives);
        Assert.NotNull(destination.ArchiveKey);
        Assert.Matches("^[0-9a-f]{64}$", destination.ArchiveKey!);
    }

    /// <summary>The answer carries no payload, so the key cannot ride out in a response.</summary>
    [Fact]
    public async Task The_answer_is_a_bare_result_with_nothing_that_could_carry_the_key()
    {
        await using var world = World.New();

        var result = await world.HandleAsync(enabled: true);

        Assert.IsType<Result>(result);
    }

    /// <summary>Switching it off keeps the key so backups sealed meanwhile stay restorable.</summary>
    [Fact]
    public async Task Disabling_keeps_the_key_that_opens_the_backups_already_sealed()
    {
        await using var world = World.New();
        await world.HandleAsync(enabled: true);
        var minted = (await world.DestinationAsync()).ArchiveKey;

        var result = await world.HandleAsync(enabled: false);

        Assert.True(result.IsSuccess);
        var destination = await world.DestinationAsync();
        Assert.False(destination.EncryptArchives);
        Assert.Equal(minted, destination.ArchiveKey);
    }

    /// <summary>Switching it on again reuses the key; a new one would strand what was sealed in between.</summary>
    [Fact]
    public async Task Enabling_again_reuses_the_same_key()
    {
        await using var world = World.New();
        await world.HandleAsync(enabled: true);
        var minted = (await world.DestinationAsync()).ArchiveKey;
        await world.HandleAsync(enabled: false);

        await world.HandleAsync(enabled: true);

        var destination = await world.DestinationAsync();
        Assert.True(destination.EncryptArchives);
        Assert.Equal(minted, destination.ArchiveKey);
    }

    /// <summary>A destination that is not there is a not-found, and the attempt is on the record.</summary>
    [Fact]
    public async Task A_destination_that_does_not_exist_is_not_found_and_audited_as_failed()
    {
        await using var world = World.New();
        var unknown = Guid.NewGuid();

        var result = await world.Handler.HandleAsync(
            new SetBackupDestinationEncryptionCommand(true, unknown, CallerAddress, "agent"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupDestinationNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupDestinationEncryptionChanged, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A destination of a kind this build cannot write is refused, whatever the setting.</summary>
    [Fact]
    public async Task A_remote_destination_is_refused_as_unsupported_and_left_unchanged()
    {
        await using var world = World.New();
        var remote = new BackupDestination(
            Guid.NewGuid(),
            "Off-site",
            BackupDestinationKind.S3,
            string.Empty,
            isDefault: false,
            BackupsTestContext.SeedInstant);
        world.DbContext.BackupDestinations.Add(remote);
        await world.DbContext.SaveChangesAsync();

        var result = await world.Handler.HandleAsync(
            new SetBackupDestinationEncryptionCommand(true, remote.Id, CallerAddress, "agent"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupDestinationRemoteUnsupported", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.False(remote.EncryptArchives);
        Assert.Null(remote.ArchiveKey);
    }

    /// <summary>Success is audited with the destination's name and carries nothing of the key.</summary>
    [Fact]
    public async Task A_change_is_audited_naming_the_destination_and_never_the_key()
    {
        await using var world = World.New();

        await world.HandleAsync(enabled: true);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupDestinationEncryptionChanged, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal(CallerAddress, entry.IpAddress);
        var key = (await world.DestinationAsync()).ArchiveKey!;
        Assert.DoesNotContain(key, entry.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>The doubles and the handler, assembled once per test.</summary>
    private sealed class World : IAsyncDisposable
    {
        /// <summary>The database, holding the seeded default destination.</summary>
        public BackupsDbContext DbContext { get; }

        /// <summary>The handler under test.</summary>
        public SetBackupDestinationEncryptionCommandHandler Handler { get; }

        /// <summary>The journal double, holding what was recorded.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>Assembles the handler over doubles.</summary>
        private World()
        {
            var currentUser = FakeCurrentUser.Admin();
            DbContext = BackupsTestContext.CreateSeeded(currentUser);
            Audit = new RecordingAuditWriter();
            Handler = new SetBackupDestinationEncryptionCommandHandler(
                DbContext,
                new ArchiveKeyGenerator(),
                new BackupAuditJournal(Audit, currentUser));
        }

        /// <summary>Assembles a world.</summary>
        /// <returns>The assembled world.</returns>
        public static World New()
        {
            return new World();
        }

        /// <summary>Runs the command against the seeded default destination.</summary>
        /// <param name="enabled">Whether new backups are sealed.</param>
        /// <returns>The handler's answer.</returns>
        public Task<Result> HandleAsync(bool enabled)
        {
            return Handler.HandleAsync(
                new SetBackupDestinationEncryptionCommand(
                    enabled, BackupDestination.DefaultDestinationId, CallerAddress, "agent"),
                CancellationToken.None);
        }

        /// <summary>Reads the seeded default destination as the database now holds it.</summary>
        /// <returns>The destination.</returns>
        public async Task<BackupDestination> DestinationAsync()
        {
            return await DbContext.BackupDestinations.SingleAsync(
                destination => destination.Id == BackupDestination.DefaultDestinationId);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
        }
    }
}
