using Maran.Modules.Dns.Domain.Entities;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Dns.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="DnsSettings"/> onto the <c>dns.DnsSettings</c> table.</summary>
public sealed class DnsSettingsConfiguration : IEntityTypeConfiguration<DnsSettings>
{
    /// <summary>The longest textual IPv6 address (with an embedded IPv4 tail), in characters.</summary>
    private const int AddressMaxLength = 45;

    /// <summary>Configures the table, the fixed key and the column constraints for <see cref="DnsSettings"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<DnsSettings> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("DnsSettings");

        // The primary key is what forbids a second row: every write addresses DnsSettings.SingletonId.
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.PrimaryNameserver)
            .IsRequired()
            .HasMaxLength(HostNameRule.MaximumLength);
        builder.Property(settings => settings.SecondaryNameserver)
            .IsRequired()
            .HasMaxLength(HostNameRule.MaximumLength);
        builder.Property(settings => settings.HostmasterMailbox)
            .IsRequired()
            .HasMaxLength(HostNameRule.MaximumLength);
        builder.Property(settings => settings.PublicIpv4).IsRequired().HasMaxLength(AddressMaxLength);
        builder.Property(settings => settings.PublicIpv6).IsRequired().HasMaxLength(AddressMaxLength);
        builder.Property(settings => settings.DefaultTtl).IsRequired();
        builder.Property(settings => settings.Enabled).IsRequired();
        builder.Property(settings => settings.DefaultTemplateId);
        builder.Property(settings => settings.UpdatedAt).IsRequired();
    }
}
