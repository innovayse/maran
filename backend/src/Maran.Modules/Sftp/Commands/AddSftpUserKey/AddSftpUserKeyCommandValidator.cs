using FluentValidation;
using Maran.Modules.Sftp.Domain.Policies;
using Maran.Modules.Sftp.Domain.ValueObjects;
using Maran.Modules.Sftp.Resources;

namespace Maran.Modules.Sftp.Commands.AddSftpUserKey;

/// <summary>
/// Validates <see cref="AddSftpUserKeyCommand"/> before it reaches the handler (rules/security.md
/// "Input"). The key is checked with the panel's one grammar, <see cref="SftpPublicKey"/>, which
/// mirrors the agent's; the agent validates every key again, so this is the customer's precise,
/// localized answer and never the only wall.
/// </summary>
/// <remarks>
/// Each message is a bare resx key, not an English sentence: <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against the module's resources. A failure names the field, never the value, so a rejected key is
/// not echoed back into a response or a log.
/// </remarks>
public sealed class AddSftpUserKeyCommandValidator : AbstractValidator<AddSftpUserKeyCommand>
{
    /// <summary>Configures the field rules for <see cref="AddSftpUserKeyCommand"/>.</summary>
    public AddSftpUserKeyCommandValidator()
    {
        RuleFor(command => command.Label)
            .Must(IsValidLabel)
            .WithMessage(nameof(ErrorMessages.SftpKeyLabelInvalid));

        RuleFor(command => command.PublicKey)
            .Must(IsValidKey)
            .WithMessage(nameof(ErrorMessages.SftpKeyInvalid));
    }

    /// <summary>Whether a label, once trimmed, is 1 to the limit in length and free of control characters.</summary>
    /// <param name="label">The label as supplied.</param>
    /// <returns>True when the entity would accept it.</returns>
    private static bool IsValidLabel(string? label)
    {
        if (label is null)
        {
            return false;
        }

        var trimmed = label.Trim();

        return trimmed.Length > 0
            && trimmed.Length <= SftpKeyLimits.MaxLabelLength
            && !trimmed.Any(char.IsControl);
    }

    /// <summary>Whether the pasted text parses as a key the agent will also accept.</summary>
    /// <param name="publicKey">The pasted key line.</param>
    /// <returns>True when <see cref="SftpPublicKey.Parse"/> accepts it.</returns>
    private static bool IsValidKey(string? publicKey)
    {
        return SftpPublicKey.Parse(publicKey).IsSuccess;
    }
}
