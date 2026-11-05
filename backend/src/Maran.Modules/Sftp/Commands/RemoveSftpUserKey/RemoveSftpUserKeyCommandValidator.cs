using FluentValidation;

namespace Maran.Modules.Sftp.Commands.RemoveSftpUserKey;

/// <summary>
/// Validates <see cref="RemoveSftpUserKeyCommand"/>. Both identifiers come from the route, so the only
/// thing left to refuse is an empty one; whether the key belongs to the login is the handler's answer,
/// and is a 404 rather than a 400.
/// </summary>
public sealed class RemoveSftpUserKeyCommandValidator : AbstractValidator<RemoveSftpUserKeyCommand>
{
    /// <summary>Configures the field rules for <see cref="RemoveSftpUserKeyCommand"/>.</summary>
    public RemoveSftpUserKeyCommandValidator()
    {
        RuleFor(command => command.SftpUserId)
            .NotEmpty();

        RuleFor(command => command.KeyId)
            .NotEmpty();
    }
}
