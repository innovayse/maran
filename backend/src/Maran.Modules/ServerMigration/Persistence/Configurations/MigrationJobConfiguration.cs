using Maran.Modules.ServerMigration.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MigrationJob"/>.</summary>
public sealed class MigrationJobConfiguration : IEntityTypeConfiguration<MigrationJob>
{
    /// <summary>The length of the column an enum is stored by name in.</summary>
    private const int EnumNameMaxLength = 32;

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<MigrationJob> builder)
    {
        builder.ToTable("MigrationJobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Property(job => job.RemoteServerId).IsRequired();
        builder.Property(job => job.Name).IsRequired().HasMaxLength(MigrationJob.NameMaxLength);
        builder.Property(job => job.State).IsRequired().HasConversion<string>().HasMaxLength(EnumNameMaxLength);
        builder.Property(job => job.Concurrency).IsRequired();
        builder.Property(job => job.BandwidthLimitKib);
        builder.Property(job => job.SourceUrl).HasMaxLength(MigrationJob.SourceUrlMaxLength);
        builder.Property(job => job.ExpectedKeyFingerprint).HasMaxLength(MigrationJob.FingerprintLength);
        builder.Property(job => job.CreatedByUserId).IsRequired();
        builder.Property(job => job.CreatedAt).IsRequired();
        builder.Property(job => job.StartedAt);
        builder.Property(job => job.FinishedAt);

        // Restrict: a server a job reads from is not removed (RemoveRemoteServerCommandHandler answers
        // RemoteServerInUse), so the database refuses it too, and a job's history never goes with a server.
        builder.HasOne<RemoteServer>()
            .WithMany()
            .HasForeignKey(job => job.RemoteServerId)
            .HasConstraintName("FK_MigrationJobs_RemoteServers_RemoteServerId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(job => job.RemoteServerId).HasDatabaseName("IX_MigrationJobs_RemoteServerId");
        builder.HasIndex(job => job.CreatedAt).HasDatabaseName("IX_MigrationJobs_CreatedAt");
    }
}
