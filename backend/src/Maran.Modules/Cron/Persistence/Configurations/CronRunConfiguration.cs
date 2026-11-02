using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Domain.Enums;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Cron.Persistence.Configurations;

/// <summary>Maps <see cref="CronRun"/> onto <c>cron.CronRuns</c>.</summary>
public sealed class CronRunConfiguration : IEntityTypeConfiguration<CronRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CronRun> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("CronRuns");
        builder.HasKey(run => run.Id);

        builder.Property(run => run.AccountId).IsRequired();
        builder.Property(run => run.EntryId).IsRequired().HasMaxLength(CronRun.EntryIdMaxLength);
        builder.Property(run => run.StartedAt).IsRequired();
        builder.Property(run => run.DurationMilliseconds).IsRequired();

        // The one stored state: the kind of ending, plus the single nullable column that kind carries.
        // The default exists only so the column can be added to a table that already holds rows (the
        // backfill below then sets every row's real value); the entity always writes the kind, and the
        // enum has no zero member, so EF never mistakes a real value for "unset".
        builder.Property(run => run.EndKind).IsRequired().HasDefaultValue(CronRunEndKind.WithoutStatus);
        builder.Property(run => run.EndExitCode);
        builder.Property(run => run.EndLimit);

        // Derived on read from the stored end; neither is a column.
        builder.Ignore(run => run.End);
        builder.Ignore(run => run.Outcome);

        // Expand phase: the three columns the previous release reads stay, under their own names, and the
        // entity writes them from the same end as the new ones. This release reads none of them; a later
        // contract-phase release drops them.
        builder.Property<int?>("LegacyExitCode").HasColumnName("ExitCode");
        builder.Property<CronRunLimit?>("LegacyLimit").HasColumnName("Limit");
        builder.Property<CronRunOutcome>("LegacyOutcome").HasColumnName("Outcome").IsRequired();

        // Backfill for the migration that adds EndKind, EndExitCode and EndLimit (put in its Up, after the
        // AddColumn calls, with a symmetric no-op Down since the old columns were never touched):
        //
        //   // raw-sql: copy the stored end of rows written by the previous release into the new columns.
        //   migrationBuilder.Sql(@"
        //       UPDATE cron.""CronRuns""
        //       SET ""EndKind"" = CASE
        //               WHEN ""Limit"" IS NOT NULL THEN 3      -- CronRunEndKind.Stopped
        //               WHEN ""ExitCode"" IS NOT NULL THEN 1   -- CronRunEndKind.Exited
        //               ELSE 2                                 -- CronRunEndKind.WithoutStatus
        //           END,
        //           ""EndExitCode"" = CASE WHEN ""Limit"" IS NULL THEN ""ExitCode"" END,
        //           ""EndLimit"" = ""Limit"";");

        // Unbounded text: the agent caps each stream at 64 KiB before the panel ever sees it, so the
        // bound is the agent's and a column length here would only be a second, drifting copy of it.
        builder.Property(run => run.Stdout).IsRequired();
        builder.Property(run => run.Stderr).IsRequired();

        // Every read is "this account's runs of this entry, newest first", and so is the retention
        // sweep that follows every insert.
        builder.HasIndex(run => new { run.AccountId, run.EntryId, run.StartedAt })
            .HasDatabaseName("IX_CronRuns_AccountId_EntryId_StartedAt");
    }
}
