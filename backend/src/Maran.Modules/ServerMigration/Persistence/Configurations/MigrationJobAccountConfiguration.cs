using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MigrationJobAccount"/>; the grant and the issued passwords are encrypted at rest.</summary>
public sealed class MigrationJobAccountConfiguration : IEntityTypeConfiguration<MigrationJobAccount>
{
    /// <summary>The length of the column an enum is stored by name in.</summary>
    private const int EnumNameMaxLength = 32;

    /// <summary>The cipher the secret columns are encrypted with on write and decrypted with on read.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration.</summary>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public MigrationJobAccountConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<MigrationJobAccount> builder)
    {
        builder.ToTable("MigrationJobAccounts");
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();
        builder.Property(account => account.JobId).IsRequired();
        builder.Property(account => account.SourceAccountName).IsRequired().HasMaxLength(MigrationJobAccount.NameMaxLength);
        builder.Property(account => account.SourceHome).IsRequired().HasMaxLength(MigrationJobAccount.HomeMaxLength);
        builder.Property(account => account.SourceDiskBytes);
        builder.Property(account => account.CronEntryCount).IsRequired();
        builder.Property(account => account.MailboxCount).IsRequired();
        builder.Property(account => account.InventoryJson).IsRequired();
        builder.Property(account => account.PlanId);
        builder.Property(account => account.TargetResellerId);
        builder.Property(account => account.OwnerEmail).HasMaxLength(MigrationJobAccount.EmailMaxLength);
        builder.Property(account => account.OwnerLanguage).IsRequired().HasMaxLength(MigrationJobAccount.LanguageMaxLength);
        builder.Property(account => account.PrimaryDomain).HasMaxLength(MigrationJobAccount.DomainMaxLength);
        // The `!` states what EF Core guarantees and the type system cannot: a value converter is never handed a
        // null, while this property is nullable because "no grant held" is a real state.
        builder.Property(account => account.PullGrant).HasConversion(new EncryptedStringConverter(_encryptionService)!);
        builder.Property(account => account.State).IsRequired().HasConversion<string>().HasMaxLength(EnumNameMaxLength);
        builder.Property(account => account.Stage).HasMaxLength(MigrationJobAccount.StageMaxLength);
        builder.Property(account => account.FailureCode).HasMaxLength(MigrationJobAccount.FailureCodeMaxLength);
        builder.Property(account => account.TargetAccountId);
        builder.Property(account => account.Provisioned).IsRequired();
        builder.Property(account => account.SlicesCompleted).IsRequired();
        builder.Property(account => account.SourceBytes);
        builder.Property(account => account.DatabasesLoaded).IsRequired();
        builder.Property(account => account.DumpBytes).IsRequired();
        builder.Property(account => account.StartedAt);
        builder.Property(account => account.SyncedAt);
        builder.Property(account => account.CutOverAt);
        builder.Property(account => account.FinishedAt);
        builder.Property(account => account.ReportJson);
        // The `!` states what EF Core guarantees and the type system cannot: a value converter is never handed a
        // null, while this property is nullable because "nothing issued yet" is a real state.
        builder.Property(account => account.IssuedSecretsJson).HasConversion(new EncryptedStringConverter(_encryptionService)!);

        builder.HasOne<MigrationJob>()
            .WithMany()
            .HasForeignKey(account => account.JobId)
            .HasConstraintName("FK_MigrationJobAccounts_MigrationJobs_JobId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(account => new { account.JobId, account.SourceAccountName })
            .IsUnique()
            .HasDatabaseName("IX_MigrationJobAccounts_JobId_SourceAccountName");
        builder.HasIndex(account => new { account.JobId, account.State })
            .HasDatabaseName("IX_MigrationJobAccounts_JobId_State");
    }
}
