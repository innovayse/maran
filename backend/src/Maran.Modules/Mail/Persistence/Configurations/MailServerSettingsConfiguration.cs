using Maran.Modules.Mail.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Mail.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MailServerSettings"/> onto the <c>mail.MailServerSettings</c> table.</summary>
public sealed class MailServerSettingsConfiguration : IEntityTypeConfiguration<MailServerSettings>
{
    /// <summary>The longest host name, in characters.</summary>
    private const int HostnameMaxLength = 253;

    /// <summary>Configures the table, key and column constraints for <see cref="MailServerSettings"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MailServerSettings> builder)
    {
        // PascalCase, explicit (rules/csharp.md "Database naming: PascalCase everywhere").
        builder.ToTable("MailServerSettings");

        // The key is what forbids a second row: every write addresses MailServerSettings.SingletonId.
        builder.HasKey(settings => settings.Id);
        builder.Property(settings => settings.Id).ValueGeneratedNever();

        builder.Property(settings => settings.Enabled).IsRequired();
        builder.Property(settings => settings.Hostname).IsRequired().HasMaxLength(HostnameMaxLength);
        builder.Property(settings => settings.Pop3Enabled).IsRequired();
        builder.Property(settings => settings.AntivirusEnabled).IsRequired();
        builder.Property(settings => settings.AllowExternalForwarding).IsRequired();
        builder.Property(settings => settings.MaxMessageMib).IsRequired();
        builder.Property(settings => settings.SpamGreylistScore).IsRequired();
        builder.Property(settings => settings.SpamAddHeaderScore).IsRequired();
        builder.Property(settings => settings.SpamRejectScore).IsRequired();
        builder.Property(settings => settings.UpdatedAt).IsRequired();
    }
}
