using FluentValidation;

namespace Maran.Modules.ServerMigration.Commands.CreateAccountExport;

/// <summary>Checks the account named by <see cref="CreateAccountExportCommand"/> is an id.</summary>
public sealed class CreateAccountExportCommandValidator : AbstractValidator<CreateAccountExportCommand>
{
    /// <summary>Declares the rules.</summary>
    public CreateAccountExportCommandValidator()
    {
        RuleFor(command => command.AccountId).NotEmpty();
    }
}
