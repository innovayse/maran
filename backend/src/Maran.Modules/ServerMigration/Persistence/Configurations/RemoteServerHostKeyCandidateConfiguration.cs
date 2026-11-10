using Maran.Modules.ServerMigration.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="RemoteServerHostKeyCandidate"/> onto <c>servermigration.RemoteServerHostKeyCandidates</c>.</summary>
public sealed class RemoteServerHostKeyCandidateConfiguration : IEntityTypeConfiguration<RemoteServerHostKeyCandidate>
{
    /// <summary>Configures the table, columns and the index that keeps one fingerprint once per server.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<RemoteServerHostKeyCandidate> builder)
    {
        builder.ToTable("RemoteServerHostKeyCandidates");

        builder.HasKey(candidate => candidate.Id);
        builder.Property(candidate => candidate.Id).ValueGeneratedNever();

        builder.Property(candidate => candidate.Algorithm).IsRequired().HasMaxLength(RemoteServer.HostKeyAlgorithmMaxLength);
        builder.Property(candidate => candidate.Key).IsRequired().HasMaxLength(RemoteServer.HostKeyMaxLength);
        builder.Property(candidate => candidate.Fingerprint).IsRequired().HasMaxLength(RemoteServer.FingerprintLength);

        builder.HasIndex(candidate => new { candidate.RemoteServerId, candidate.Fingerprint })
            .IsUnique()
            .HasDatabaseName("IX_RemoteServerHostKeyCandidates_RemoteServerId_Fingerprint");
    }
}
