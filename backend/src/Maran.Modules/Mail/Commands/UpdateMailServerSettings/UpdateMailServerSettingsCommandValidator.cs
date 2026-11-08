using Maran.Modules.Mail.Validators;

namespace Maran.Modules.Mail.Commands.UpdateMailServerSettings;

/// <summary>
/// Validates <see cref="UpdateMailServerSettingsCommand"/> with the rules every mail server settings input obeys.
/// </summary>
public sealed class UpdateMailServerSettingsCommandValidator : MailServerSettingsInputValidator<UpdateMailServerSettingsCommand>
{
}
