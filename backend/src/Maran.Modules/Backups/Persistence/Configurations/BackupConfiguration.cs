using Maran.Modules.Backups.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Backups.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="Backup"/> onto the <c>backups.Backups</c> table.</summary>
public sealed class BackupConfiguration : IEntityTypeConfiguration<Backup>
{
    /// <summary>The length of a SHA-256 rendered as lowercase hexadecimal.</summary>
    private const int Sha256HexLength = 64;

    /// <summary>The longest failure CODE this table stores; codes are identifiers, not sentences.</summary>
    private const int FailureCodeMaxLength = 64;

    /// <summary>The longest an enum member's persisted name may be.</summary>
    private const int EnumNameMaxLength = 16;

    /// <summary>The longest system user name a deleted account can have left behind.</summary>
    /// <remarks>The ceiling the panel already validates account names against.</remarks>
    private const int UsernameMaxLength = 32;

    /// <summary>Configures the table, keys, and column constraints for <see cref="Backup"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<Backup> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") — never
        // the provider's lowercase default. Column names below match the property names exactly, so
        // they are left to EF Core's default rather than repeated via HasColumnName.
        builder.ToTable("Backups");
        builder.HasKey(backup => backup.Id);

        builder.Property(backup => backup.AccountId)
            .IsRequired();

        // Still nullable, and deliberately not narrowed. Rows written before the destination table
        // existed carry null and it means "the configured local root", which is now the default
        // destination row — so the null keeps the meaning it was written with. Narrowing the column
        // would be an AlterColumn against a release that writes null on every insert, which is the
        // expand-then-contract law's refusal and not a marker anyone could honestly write
        // (rules/architecture.md). Every NEW row carries the resolved destination's id.
        builder.Property(backup => backup.DestinationId);

        // Declared now that the table exists, and Restrict rather than a cascade: deleting a
        // destination out from under the rows that name it would leave backups pointing nowhere and
        // an artifact on a disk nothing could address. There is no delete endpoint, so this is the
        // guard against a hand-written one.
        builder.HasOne<BackupDestination>()
            .WithMany()
            .HasForeignKey(backup => backup.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Stored as the member's NAME rather than its number, so a row read in psql says what it is
        // and adding a member later cannot renumber what is already written.
        builder.Property(backup => backup.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(backup => backup.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(EnumNameMaxLength);

        builder.Property(backup => backup.SizeBytes)
            .IsRequired();

        // Expand phase of replacing three empty-string sentinels with nullable columns. Each fact
        // below is added as a NEW nullable column the code reads; the previous release's column stays
        // (mapped as a private Legacy… property, written by the entity in step, read by nothing) until
        // a later release drops it.
        //
        // raw-sql backfill, written into the migration's Up right after the columns are added
        // (identifiers quoted: PascalCase, rules/csharp.md). No check constraint ties the new columns
        // to Status on purpose: the previous release completes a row by writing only the old columns,
        // and a constraint over the new ones would refuse that write after a rollback. Re-running the
        // backfill is harmless, and it is what makes rows the previous release wrote readable again:
        //   UPDATE backups."Backups" SET "ArtifactSha256" = NULLIF("Sha256", '')
        //     WHERE "Status" = 'Completed' AND "ArtifactSha256" IS NULL;
        //   UPDATE backups."Backups" SET "RunFailureCode" = NULLIF("FailureCode", '')
        //     WHERE "Status" = 'Failed' AND "RunFailureCode" IS NULL;
        //   UPDATE backups."Backups" SET "DeletedAccountUsername" = NULLIF("OrphanedAccountUsername", '')
        //     WHERE "DeletedAccountUsername" IS NULL;
        // Down needs no data statement: the previous release reads the old columns, which stay written.
        //
        // Fixed at the digest's own width. A column that could hold more is a column something other
        // than a digest can be written into, and this value is what a restore's integrity check is
        // decided by.
        builder.Property(backup => backup.ArtifactSha256)
            .HasMaxLength(Sha256HexLength);

        builder.Property<string>("LegacySha256")
            .HasColumnName("Sha256")
            .IsRequired()
            .HasMaxLength(Sha256HexLength);

        builder.Property(backup => backup.DatabaseCount)
            .IsRequired();

        builder.Property(backup => backup.StartedAt)
            .IsRequired();

        builder.Property(backup => backup.FinishedAt);

        // An error CODE, so the column is short by design. Widening it is how the agent's own
        // sentence — which can quote a path or a database name — would end up in this table
        // (rules/security.md item 8).
        builder.Property(backup => backup.RunFailureCode)
            .HasMaxLength(FailureCodeMaxLength);

        builder.Property<string>("LegacyFailureCode")
            .HasColumnName("FailureCode")
            .IsRequired()
            .HasMaxLength(FailureCodeMaxLength);

        // Null while the account exists; the deleted account's name once it does not. The previous
        // release's column below was required-with-an-empty-default, which made "no deleted account"
        // a string of length zero; it is still written (as that empty default) and read by nothing.
        builder.Property(backup => backup.DeletedAccountUsername)
            .HasMaxLength(UsernameMaxLength);

        builder.Property<string>("LegacyOrphanedAccountUsername")
            .HasColumnName("OrphanedAccountUsername")
            .IsRequired()
            .HasMaxLength(UsernameMaxLength)
            .HasDefaultValue(string.Empty);

        // The listing every screen asks for: one account's backups, newest first. Composite rather
        // than an index on AccountId alone, because the global query filter supplies the account
        // predicate on every read and the ordering is what the rest of the query costs.
        builder.HasIndex(backup => new { backup.AccountId, backup.StartedAt })
            .HasDatabaseName("IX_Backups_AccountId_StartedAt");

        // The administrator's listing has no account predicate, so the composite above cannot order
        // it: its default sort (newest first) and its start-time window both read the whole journal
        // and sort it without this one. A journal that grows with every account every day is the
        // table where that cost keeps rising (issue #196, docs/performance/list-indexes.md).
        builder.HasIndex(backup => backup.StartedAt)
            .HasDatabaseName("IX_Backups_StartedAt");
    }
}
