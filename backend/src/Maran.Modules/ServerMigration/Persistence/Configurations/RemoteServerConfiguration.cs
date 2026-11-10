using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Policies;
using Maran.SharedKernel.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="RemoteServer"/> onto the <c>servermigration.RemoteServers</c> table.</summary>
/// <remarks>
/// Two unique indexes are the registry's integrity: two servers cannot share the label an administrator picks them
/// by, and two rows cannot register the same host, port and login, which would be one source twice. The private
/// key column is encrypted at rest through the panel's cipher and is the only secret the module stores.
/// </remarks>
public sealed class RemoteServerConfiguration : IEntityTypeConfiguration<RemoteServer>
{
    /// <summary>The widest an enum stored by name is.</summary>
    private const int EnumNameMaxLength = 32;

    /// <summary>The cipher the private key column is encrypted with on write and decrypted with on read.</summary>
    private readonly IEncryptionService _encryptionService;

    /// <summary>Creates the configuration.</summary>
    /// <param name="encryptionService">The panel's shared cipher for secrets at rest.</param>
    public RemoteServerConfiguration(IEncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>Configures the table, columns, the encrypted key, the cascade to the host key candidates and the indexes.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<RemoteServer> builder)
    {
        builder.ToTable("RemoteServers");

        // Assigned by the application and never generated.
        builder.HasKey(server => server.Id);
        builder.Property(server => server.Id).ValueGeneratedNever();

        builder.Property(server => server.Name).IsRequired().HasMaxLength(RemoteServerFieldLimits.NameMaxLength);
        builder.Property(server => server.Host).IsRequired().HasMaxLength(RemoteServerFieldLimits.HostMaxLength);
        builder.Property(server => server.Port).IsRequired();
        builder.Property(server => server.SshUser).IsRequired().HasMaxLength(RemoteServerFieldLimits.SshUserMaxLength);
        builder.Property(server => server.AllowedFrom).HasMaxLength(RemoteServerFieldLimits.AllowedFromMaxLength);
        builder.Property(server => server.PublicKey).IsRequired().HasMaxLength(RemoteServer.PublicKeyMaxLength);

        // Encrypted at rest through the panel's shared cipher, never plaintext (rules/security.md item 8). The
        // column is wider than the key because ciphertext is longer than the document it protects.
        builder.Property(server => server.PrivateKeyCipher)
            .IsRequired()
            .HasMaxLength(RemoteServer.PrivateKeyColumnLength)
            .HasConversion(new EncryptedStringConverter(_encryptionService));

        builder.Property(server => server.HostKeyAlgorithm).HasMaxLength(RemoteServer.HostKeyAlgorithmMaxLength);
        builder.Property(server => server.HostKey).HasMaxLength(RemoteServer.HostKeyMaxLength);
        builder.Property(server => server.HostKeyFingerprint).HasMaxLength(RemoteServer.FingerprintLength);
        builder.Property(server => server.HostKeyConfirmedAt);

        builder.Property(server => server.State).IsRequired().HasConversion<string>().HasMaxLength(EnumNameMaxLength);
        builder.Property(server => server.LastTestedAt);
        builder.Property(server => server.LastFailure).HasConversion<string>().HasMaxLength(EnumNameMaxLength);
        builder.Property(server => server.Kind).HasConversion<string>().HasMaxLength(EnumNameMaxLength);
        builder.Property(server => server.SystemRelease).HasMaxLength(RemoteServer.SystemReleaseMaxLength);
        builder.Property(server => server.MaranVersion).HasMaxLength(RemoteServer.MaranVersionMaxLength);
        builder.Property(server => server.IsRoot);
        builder.Property(server => server.HomeFreeBytes);
        builder.Property(server => server.HomeTotalBytes);
        builder.Property(server => server.DatabaseServer).HasMaxLength(RemoteServer.ServerNameMaxLength);
        builder.Property(server => server.WebServer).HasMaxLength(RemoteServer.ServerNameMaxLength);
        builder.Property(server => server.MailFound);
        builder.Property(server => server.DiscoveredAt);
        builder.Property(server => server.AccountsFound);
        builder.Property(server => server.CreatedAt).IsRequired();

        // Cascade: the offered keys exist for exactly as long as the server does.
        builder.HasMany(server => server.HostKeyCandidates)
            .WithOne()
            .HasForeignKey(candidate => candidate.RemoteServerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(server => server.Name)
            .IsUnique()
            .HasDatabaseName("IX_RemoteServers_Name");

        builder.HasIndex(server => new { server.Host, server.Port, server.SshUser })
            .IsUnique()
            .HasDatabaseName("IX_RemoteServers_Endpoint");
    }
}
