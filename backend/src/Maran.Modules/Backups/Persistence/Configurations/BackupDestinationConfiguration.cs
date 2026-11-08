using Maran.Modules.Backups.Domain.Entities;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Backups.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="BackupDestination"/> onto the <c>backups.BackupDestinations</c>
/// table.
/// </summary>
public sealed class BackupDestinationConfiguration : IEntityTypeConfiguration<BackupDestination>
{
    /// <summary>The longest an enum member's persisted name may be.</summary>
    private const int EnumNameMaxLength = 16;

    /// <summary>
    /// The longest the stored archive key can be: a 64-character key through the panel's AES-GCM
    /// cipher is a 12-byte nonce, a 16-byte tag and 64 bytes of ciphertext, 92 bytes, which is 124
    /// base64 characters. Rounded up so a longer nonce or a different cipher does not need a column
    /// change.
    /// </summary>
    private const int ArchiveKeyMaxLength = 160;

    /// <summary>The cipher the archive key is stored under.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration.</summary>
    /// <param name="encryptionService">The cipher applied to the archive key column.</param>
    public BackupDestinationConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>Configures the table, keys, and column constraints for <see cref="BackupDestination"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<BackupDestination> builder)
    {
        builder.ToTable("BackupDestinations");
        builder.HasKey(destination => destination.Id);

        builder.Property(destination => destination.Name)
            .IsRequired()
            .HasMaxLength(BackupDestination.NameMaxLength);

        // Stored as the member's NAME rather than its number, so a row read in psql says what it is
        // and adding a member later cannot renumber what is already written.
        builder.Property(destination => destination.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        // Required-with-an-empty-default rather than nullable, so "this kind records no path" has one
        // representation and a reader never has to decide whether NULL and '' mean the same thing.
        builder.Property(destination => destination.Path)
            .IsRequired()
            .HasMaxLength(BackupDestination.PathMaxLength)
            .HasDefaultValue(string.Empty);

        builder.Property(destination => destination.IsDefault)
            .IsRequired();

        builder.Property(destination => destination.CreatedAt)
            .IsRequired();

        // EXPAND-ONLY: required with a database default so the additive migration could add it to a
        // table that already has its one row, and so the previous release, which does not know the
        // column, still inserts destinations.
        builder.Property(destination => destination.EncryptArchives)
            .IsRequired()
            .HasDefaultValue(false);

        // The key that opens this destination's sealed archives, at rest as ciphertext under the
        // panel's own key (rules/security.md item 8). Nullable because "encryption was never switched
        // on here" is a real state; the `!` states what EF Core guarantees and the type system cannot:
        // a value converter is never handed a null (the same arrangement as the TOTP secret's).
        builder.Property(destination => destination.ArchiveKey)
            .HasConversion(new EncryptedStringConverter(_encryptionService)!)
            .HasMaxLength(ArchiveKeyMaxLength);

        builder.HasIndex(destination => destination.Name)
            .IsUnique()
            .HasDatabaseName("IX_BackupDestinations_Name");

        // Filtered, so the uniqueness is "at most one DEFAULT" rather than "at most two rows". A
        // plain unique index on a boolean would cap the whole table at two, which is a constraint
        // about the wrong thing that would read as this one.
        builder.HasIndex(destination => destination.IsDefault)
            .IsUnique()
            .HasFilter("\"IsDefault\"")
            .HasDatabaseName("IX_BackupDestinations_IsDefault");
    }
}
