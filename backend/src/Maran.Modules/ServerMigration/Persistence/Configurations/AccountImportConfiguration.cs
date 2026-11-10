using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AccountImport"/> onto <c>servermigration.AccountImports</c>.</summary>
/// <remarks>
/// The state is stored by name. The generated credentials column is encrypted at rest through the panel's
/// cipher, and is cleared when the administrator claims them. The account the import creates is held as
/// <c>TargetAccountId</c> and carries no tenant filter: the import is the administrator's bookkeeping.
/// </remarks>
public sealed class AccountImportConfiguration : IEntityTypeConfiguration<AccountImport>
{
    /// <summary>The widest an enum stored by name is.</summary>
    private const int EnumNameMaxLength = 32;

    /// <summary>The cipher the credentials column is encrypted with on write and decrypted with on read.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration.</summary>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public AccountImportConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>Configures the table, columns, the encrypted credentials and the index the list sorts on.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AccountImport> builder)
    {
        builder.ToTable("AccountImports");
        builder.HasKey(import => import.Id);
        builder.Property(import => import.Id).ValueGeneratedNever();
        builder.Property(import => import.State).IsRequired().HasConversion<string>().HasMaxLength(EnumNameMaxLength);
        builder.Property(import => import.SourceAccountName).IsRequired().HasMaxLength(AccountImport.NameMaxLength);
        builder.Property(import => import.SourcePrimaryDomain).IsRequired().HasMaxLength(AccountImport.NameMaxLength);
        builder.Property(import => import.SourceHostName).IsRequired().HasMaxLength(AccountImport.NameMaxLength);
        builder.Property(import => import.KeyFingerprint).IsRequired().HasMaxLength(MigrationSigningKey.FingerprintLength);
        builder.Property(import => import.SectionsJson).IsRequired();
        builder.Property(import => import.ArtifactBackupId).IsRequired();
        builder.Property(import => import.ArtifactSizeBytes).IsRequired();
        builder.Property(import => import.ArtifactSha256).IsRequired().HasMaxLength(ExportGrant.DigestLength);
        builder.Property(import => import.DatabaseCount).IsRequired();
        builder.Property(import => import.HomeBytes).IsRequired();
        builder.Property(import => import.CreatedByUserId).IsRequired();
        builder.Property(import => import.CreatedAt).IsRequired();
        builder.Property(import => import.StartedAt);
        builder.Property(import => import.FinishedAt);
        builder.Property(import => import.TargetAccountId);
        builder.Property(import => import.PlanId);
        builder.Property(import => import.TargetResellerId);
        builder.Property(import => import.Stage).HasMaxLength(AccountImport.StageMaxLength);
        builder.Property(import => import.FailureCode).HasMaxLength(AccountImport.FailureCodeMaxLength);
        // The `!` states what EF Core guarantees and the type system cannot: a value converter is never handed a
        // null, while this property is nullable because "nothing issued yet" is a real state.
        builder.Property(import => import.IssuedSecretsJson).HasConversion(new EncryptedStringConverter(_encryptionService)!);

        builder.HasIndex(import => import.CreatedAt).HasDatabaseName("IX_AccountImports_CreatedAt");
    }
}
