using Maran.Modules.Ftp.Domain.Entities;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Ftp.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="FtpsSettings"/> onto the <c>ftp.FtpsSettings</c> table.
/// </summary>
/// <remarks>
/// Read the column list for what is NOT here: no <c>AccountId</c>, because one server has one FTPS
/// daemon and this row belongs to the installation rather than to a customer; and no observed state
/// — no "running", no "certificate present" — because those are measured on the host at read time
/// and a stored copy would be a second, staler answer to a question the agent answers exactly.
/// </remarks>
public sealed class FtpsSettingsConfiguration : IEntityTypeConfiguration<FtpsSettings>
{
    /// <summary>The longest host name DNS allows, which is the ceiling on <c>Hostname</c>.</summary>
    private const int HostnameMaxLength = HostNameRule.MaximumLength;

    /// <summary>The longest textual IP address the PASV advertisement column has to hold.</summary>
    /// <remarks>
    /// Forty-five characters: the widest form an IPv6 address takes in text, the IPv4-mapped
    /// <c>ffff:</c> notation included. Sized to the value rather than to a round number so the
    /// database cannot hold something the daemon's configuration line could never carry.
    /// </remarks>
    private const int PassiveAddressMaxLength = 45;

    /// <summary>Configures the table, key, and column constraints for <see cref="FtpsSettings"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<FtpsSettings> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere") —
        // never the provider's lowercase default.
        builder.ToTable("FtpsSettings");

        // The primary key is what forbids a second row: every write addresses
        // FtpsSettings.SingletonId, so a duplicate is a key violation rather than a second opinion
        // about how this server's one daemon is configured.
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.Hostname)
            .IsRequired()
            .HasMaxLength(HostnameMaxLength);

        builder.Property(settings => settings.Enabled)
            .IsRequired();

        builder.Property(settings => settings.PassivePortMin)
            .IsRequired();

        builder.Property(settings => settings.PassivePortMax)
            .IsRequired();

        builder.Property(settings => settings.PassiveAddress)
            .IsRequired()
            .HasMaxLength(PassiveAddressMaxLength);

        builder.Property(settings => settings.MaxClients)
            .IsRequired();

        builder.Property(settings => settings.Ipv4Only)
            .IsRequired();

        builder.Property(settings => settings.UpdatedAt)
            .IsRequired();
    }
}
