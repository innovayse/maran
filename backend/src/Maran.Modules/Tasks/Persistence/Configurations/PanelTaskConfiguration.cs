using Maran.Modules.Tasks.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Tasks.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="PanelTask"/> onto the <c>tasks.PanelTasks</c> table.</summary>
public sealed class PanelTaskConfiguration : IEntityTypeConfiguration<PanelTask>
{
    /// <summary>Ceiling on a task's kind, which is a machine-stable identifier and never a sentence.</summary>
    private const int KindMaxLength = 64;

    /// <summary>Ceiling on a task's subject: a domain name is the longest thing recorded there.</summary>
    private const int SubjectMaxLength = 253;

    /// <summary>Ceiling on a correlation id, which is a GUID in every form the panel mints.</summary>
    private const int CorrelationIdMaxLength = 64;

    /// <summary>Ceiling on an error code, which is a resx key in flat PascalCase.</summary>
    private const int ErrorCodeMaxLength = 128;

    /// <summary>Configures the table, keys, and column constraints for <see cref="PanelTask"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<PanelTask> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") —
        // never the provider's lowercase default.
        builder.ToTable("PanelTasks");
        builder.HasKey(task => task.Id);

        builder.Property(task => task.Kind)
            .IsRequired()
            .HasMaxLength(KindMaxLength);

        builder.Property(task => task.Subject)
            .IsRequired()
            .HasMaxLength(SubjectMaxLength);

        builder.Property(task => task.CorrelationId)
            .HasMaxLength(CorrelationIdMaxLength);

        builder.Property(task => task.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(KindMaxLength);

        builder.Property(task => task.Percent)
            .IsRequired();

        // The column is one marker longer than the cap the entity enforces, because a capped log IS
        // the cut text plus the marker that says so. Sizing it at the cap alone would make the
        // entity's own truncated value the one string this column cannot store.
        builder.Property(task => task.Log)
            .IsRequired()
            .HasMaxLength(PanelTask.MaxLogLength + PanelTask.TruncationMarker.Length);

        builder.Property(task => task.ErrorCode)
            .HasMaxLength(ErrorCodeMaxLength);

        builder.Property(task => task.StartedAt)
            .IsRequired();

        builder.Property(task => task.Revision)
            .IsRequired();

        // The listing is "newest first", which was once the only read this table had that was not
        // by id.
        builder.HasIndex(task => task.StartedAt).HasDatabaseName("IX_PanelTasks_StartedAt");

        // The second: retention's nightly sweep selects finished rows older than its window
        // (TaskRetentionHandler). Without an index that scan is a sequential pass over the whole
        // table every night — cheap while retention keeps the table small, but the table is
        // exactly what a server that skipped this migration for a year did NOT have small, and a
        // sequential scan is also the read a running task's null FinishedAt would otherwise force
        // the planner past on every row. FinishedAt is null while Running (PanelTask never sets it
        // before Complete/Fail), so an index on it also makes "skip everything still in flight"
        // free rather than a filter applied after the fact.
        builder.HasIndex(task => task.FinishedAt).HasDatabaseName("IX_PanelTasks_FinishedAt");
    }
}
