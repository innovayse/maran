using Maran.Modules.Cron.Domain.Entities;
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
        builder.Property(run => run.Outcome).IsRequired();

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
