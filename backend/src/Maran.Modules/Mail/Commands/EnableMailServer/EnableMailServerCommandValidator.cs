using Maran.Modules.Mail.Validators;

namespace Maran.Modules.Mail.Commands.EnableMailServer;

/// <summary>
/// Validates <see cref="EnableMailServerCommand"/> with the rules every mail server settings input obeys.
/// </summary>
public sealed class EnableMailServerCommandValidator : MailServerSettingsInputValidator<EnableMailServerCommand>
{
}
