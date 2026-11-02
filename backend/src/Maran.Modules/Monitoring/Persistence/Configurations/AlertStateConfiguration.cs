using Maran.Modules.Monitoring.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Monitoring.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="AlertState"/> onto the <c>monitoring.AlertStates</c> table.</summary>
public sealed class AlertStateConfiguration : IEntityTypeConfiguration<AlertState>
{
    /// <summary>The longest subject a row can name: a mount point or a managed service's name.</summary>
    private const int SubjectMaxLength = 200;

    /// <summary>Configures the table, key, and the uniqueness that makes deduplication possible.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<AlertState> builder)
    {
        builder.ToTable("AlertStates", table =>
        {
            // The raise time exists exactly while an episode is open, and the phase is one of the
            // three it can be: the database refuses a row the entity could never have written.
            table.HasCheckConstraint(
                "CK_AlertStates_Phase_RaisedAt",
                "(\"Phase\" = 2 AND \"RaisedAt\" IS NOT NULL) OR (\"Phase\" IN (0, 1) AND \"RaisedAt\" IS NULL)");
        });
        builder.HasKey(state => state.Id);

        builder.Property(state => state.Kind)
            .IsRequired();

        builder.Property(state => state.Subject)
            .IsRequired()
            .HasMaxLength(SubjectMaxLength);

        builder.Property(state => state.ConsecutiveBreaches)
            .IsRequired();

        // Expand phase of replacing the IsFiring flag and the breach count with one state. The
        // column is added with the migration that carries the backfill below; the code reads this
        // one only.
        //
        // raw-sql backfill, written into the migration's Up right after the column is added and
        // before the check constraint above is created (identifiers quoted: PascalCase, rules/csharp.md):
        //   UPDATE monitoring."AlertStates" SET "Phase" = 2 WHERE "IsFiring";
        //   UPDATE monitoring."AlertStates" SET "Phase" = 1 WHERE NOT "IsFiring" AND "ConsecutiveBreaches" > 0;
        // Healthy (0) is the column default, so every other row needs no statement. Down needs no
        // data statement: the previous release reads IsFiring, which stays written.
        builder.Property(state => state.Phase)
            .IsRequired();

        // The previous release's flag: still mapped, still written by the entity in step with Phase,
        // never read. Dropped in a later release (contract phase).
        builder.Property<bool>("LegacyIsFiring")
            .HasColumnName("IsFiring")
            .IsRequired();

        builder.Property(state => state.LastObservedAt)
            .IsRequired();

        // One row per condition, enforced by the database rather than by the evaluator remembering.
        // Two rows for one condition are two independent counters, and the alert would then be sent
        // twice — or, worse, raised by one row and resolved by the other, so the operator reads that
        // the disk recovered while it is still full.
        builder.HasIndex(state => new { state.Kind, state.Subject })
            .IsUnique()
            .HasDatabaseName("IX_AlertStates_Kind_Subject");
    }
}
