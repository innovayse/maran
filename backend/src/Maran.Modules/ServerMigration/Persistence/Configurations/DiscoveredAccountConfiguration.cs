using Maran.Modules.ServerMigration.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.ServerMigration.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="DiscoveredAccount"/> onto <c>servermigration.DiscoveredAccounts</c>.</summary>
public sealed class DiscoveredAccountConfiguration : IEntityTypeConfiguration<DiscoveredAccount>
{
    /// <summary>Configures the table, columns, the cascades from the server and to the children, and the indexes.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DiscoveredAccount> builder)
    {
        builder.ToTable("DiscoveredAccounts");

        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();

        builder.Property(account => account.Name).IsRequired().HasMaxLength(DiscoveredAccount.NameMaxLength);
        builder.Property(account => account.Home).IsRequired().HasMaxLength(DiscoveredAccount.HomeMaxLength);
        builder.Property(account => account.DiskBytes).IsRequired();
        builder.Property(account => account.DiskKnown).IsRequired();
        builder.Property(account => account.SiteCount).IsRequired();
        builder.Property(account => account.DatabaseCount).IsRequired();
        builder.Property(account => account.CronEntryCount).IsRequired();
        builder.Property(account => account.MailboxCount).IsRequired();

        // Unbounded text by design: the domains of every site of one account, for the list's search. The agent
        // bounds the whole inventory, so the column is bounded by what a discovery can carry.
        builder.Property(account => account.DomainsText).IsRequired();

        // Cascade: the discovered rows exist for exactly as long as the server does.
        builder.HasOne<RemoteServer>()
            .WithMany()
            .HasForeignKey(account => account.RemoteServerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(account => account.Sites)
            .WithOne()
            .HasForeignKey(site => site.DiscoveredAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(account => account.Databases)
            .WithOne()
            .HasForeignKey(database => database.DiscoveredAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // One login once per server, and the read every list makes: this server's accounts by name.
        builder.HasIndex(account => new { account.RemoteServerId, account.Name })
            .IsUnique()
            .HasDatabaseName("IX_DiscoveredAccounts_RemoteServerId_Name");
    }
}
