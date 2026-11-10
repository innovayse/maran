using FluentValidation;
using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Utilities.Mail;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.ServerMigration.Commands.SaveMigrationMapping;

/// <summary>Validates <see cref="SaveMigrationMappingCommand"/> (issue #197, phase 2).</summary>
public sealed class SaveMigrationMappingCommandValidator : AbstractValidator<SaveMigrationMappingCommand>
{
    /// <summary>The languages the panel is translated into.</summary>
    private static readonly string[] Languages = ["en", "ru", "hy"];

    /// <summary>Declares the rules.</summary>
    public SaveMigrationMappingCommandValidator()
    {
        RuleFor(command => command.AccountIds).NotNull();
        RuleFor(command => command.OwnerEmail!)
            .Must(EmailAddressRule.IsAddress)
            .MaximumLength(MigrationJobAccount.EmailMaxLength)
            .When(command =>
            {
                return command.OwnerEmail is not null;
            });
        RuleFor(command => command.OwnerLanguage!)
            .Must(language =>
            {
                return Languages.Contains(language, StringComparer.Ordinal);
            })
            .When(command =>
            {
                return command.OwnerLanguage is not null;
            });
        RuleFor(command => command.PrimaryDomain!)
            .Must(HostNameRule.IsHostName)
            .When(command =>
            {
                return command.PrimaryDomain is not null;
            });
        RuleFor(command => command.AccountIds)
            .Must(ids =>
            {
                return ids.Count == 1;
            })
            .When(command =>
            {
                return command.OwnerEmail is not null || command.PrimaryDomain is not null || command.PullGrant is not null;
            });
    }
}
