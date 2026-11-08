using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Maran.Modules.Backups.Tests.Domain;

/// <summary>
/// The two rules a destination's encryption setting keeps: the key outlives the setting, and the
/// setting never refers to a key that does not exist.
/// </summary>
public sealed class BackupDestinationEncryptionTests
{
    /// <summary>A key the tests can name; any 64-character string will do for the entity.</summary>
    private static readonly string KeyOne = new('a', 64);

    /// <summary>A second key, to tell "kept" from "replaced".</summary>
    private static readonly string KeyTwo = new('b', 64);

    /// <summary>A destination that has never had encryption switched on holds no key and encrypts nothing.</summary>
    [Fact]
    public void A_new_destination_encrypts_nothing_and_holds_no_key()
    {
        var destination = NewDestination();

        Assert.False(destination.EncryptArchives);
        Assert.Null(destination.ArchiveKey);
    }

    /// <summary>Switching it on takes the key it is given.</summary>
    [Fact]
    public void Enabling_takes_the_key_it_is_given_the_first_time()
    {
        var destination = NewDestination();

        destination.EnableArchiveEncryption(KeyOne);

        Assert.True(destination.EncryptArchives);
        Assert.Equal(KeyOne, destination.ArchiveKey);
    }

    /// <summary>Switching it off keeps the key, so backups sealed meanwhile stay restorable.</summary>
    [Fact]
    public void Disabling_stops_new_backups_being_sealed_and_keeps_the_key()
    {
        var destination = NewDestination();
        destination.EnableArchiveEncryption(KeyOne);

        destination.DisableArchiveEncryption();

        Assert.False(destination.EncryptArchives);
        Assert.Equal(KeyOne, destination.ArchiveKey);
    }

    /// <summary>Switching it on again never replaces the key: the backups sealed under it would be stranded.</summary>
    [Fact]
    public void Enabling_again_keeps_the_original_key_and_ignores_the_one_offered()
    {
        var destination = NewDestination();
        destination.EnableArchiveEncryption(KeyOne);
        destination.DisableArchiveEncryption();

        destination.EnableArchiveEncryption(KeyTwo);

        Assert.True(destination.EncryptArchives);
        Assert.Equal(KeyOne, destination.ArchiveKey);
    }

    /// <summary>Enabling is idempotent.</summary>
    [Fact]
    public void Enabling_twice_changes_nothing_the_second_time()
    {
        var destination = NewDestination();
        destination.EnableArchiveEncryption(KeyOne);

        destination.EnableArchiveEncryption(KeyTwo);

        Assert.Equal(KeyOne, destination.ArchiveKey);
    }

    /// <summary>An empty key would turn on an encryption the agent reads as "plain", so it turns on nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Enabling_with_no_key_and_none_held_does_nothing(string? key)
    {
        var destination = NewDestination();

        destination.EnableArchiveEncryption(key!);

        Assert.False(destination.EncryptArchives);
        Assert.Null(destination.ArchiveKey);
    }

    /// <summary>The key's column converts it through the cipher, and the converter reverses it exactly.</summary>
    /// <remarks>
    /// The in-memory provider never applies a converter to what it stores, so a round trip through it
    /// proves nothing about encryption. What can be observed is the converter the model gives the column,
    /// which is the one Npgsql applies; the positive control is a second model built with a cipher that
    /// changes nothing, whose converter must leave the key readable. The bytes PostgreSQL stores are
    /// asserted in the integration suite (<c>BackupDestinationTests</c>).
    /// </remarks>
    [Fact]
    public async Task The_key_column_converts_through_the_cipher_and_back_as_written()
    {
        await using var sealing = BackupsTestContext.Create(FakeCurrentUser.Admin());
        await using var plain = BackupsTestContext.CreateReadingRaw(FakeCurrentUser.Admin(), Guid.NewGuid().ToString());

        var converter = ArchiveKeyConverter(sealing);
        var stored = (string)converter.ConvertToProvider(KeyOne)!;
        Assert.StartsWith(MarkingEncryptionService.Marker, stored, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyOne, stored, StringComparison.Ordinal);
        Assert.Equal(KeyOne, converter.ConvertFromProvider(stored));

        var control = (string)ArchiveKeyConverter(plain).ConvertToProvider(KeyOne)!;
        Assert.Equal(KeyOne, control);
    }

    /// <summary>The key still round-trips through the context as the entity states it.</summary>
    [Fact]
    public async Task The_key_comes_back_from_the_context_as_written()
    {
        var database = Guid.NewGuid().ToString();
        await using (var writer = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            var destination = NewDestination();
            destination.EnableArchiveEncryption(KeyOne);
            writer.BackupDestinations.Add(destination);
            await writer.SaveChangesAsync();
        }

        await using var reader = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var read = await reader.BackupDestinations.AsNoTracking().SingleAsync();
        Assert.Equal(KeyOne, read.ArchiveKey);
        Assert.True(read.EncryptArchives);
    }

    /// <summary>Reads the value converter the model gives a destination's archive key column.</summary>
    /// <param name="context">The context whose model is read.</param>
    /// <returns>The converter the column is stored through.</returns>
    private static ValueConverter ArchiveKeyConverter(BackupsDbContext context)
    {
        return context.Model.FindEntityType(typeof(BackupDestination))!
            .FindProperty(nameof(BackupDestination.ArchiveKey))!
            .GetValueConverter()!;
    }

    /// <summary>Builds a local destination the tests can change.</summary>
    /// <returns>A destination with the setting at its default.</returns>
    private static BackupDestination NewDestination()
    {
        return new BackupDestination(
            Guid.NewGuid(),
            "Local",
            BackupDestinationKind.Local,
            string.Empty,
            isDefault: true,
            BackupsTestContext.SeedInstant);
    }
}
