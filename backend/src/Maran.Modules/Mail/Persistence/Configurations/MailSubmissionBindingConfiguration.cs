using Maran.Modules.Mail.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maran.Modules.Mail.Persistence.Configurations;

/// <summary>EF Core mapping for <see cref="MailSubmissionBinding"/> onto the <c>mail.MailSubmissionBindings</c> table.</summary>
public sealed class MailSubmissionBindingConfiguration : IEntityTypeConfiguration<MailSubmissionBinding>
{
    /// <summary>The longest system user name, in characters.</summary>
    private const int UsernameMaxLength = 64;

    /// <summary>The longest stored state name, in characters.</summary>
    private const int StateMaxLength = 24;

    /// <summary>Configures the table, key and column constraints for <see cref="MailSubmissionBinding"/>.</summary>
    /// <param name="builder">The entity type builder supplied by EF Core.</param>
    public void Configure(EntityTypeBuilder<MailSubmissionBinding> builder)
    {
        builder.ToTable("MailSubmissionBindings");

        // One row per system user: the key is the agent's own key for the binding.
        builder.HasKey(binding => binding.Username);
        builder.Property(binding => binding.Username).IsRequired().HasMaxLength(UsernameMaxLength).ValueGeneratedNever();
        builder.Property(binding => binding.State).IsRequired().HasConversion<string>().HasMaxLength(StateMaxLength);
    }
}
