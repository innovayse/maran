using FluentValidation;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Extensions;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Identity.Commands.InviteSubUser;

/// <summary>Bounds the fields of <see cref="InviteSubUserCommand"/>.</summary>
public sealed class InviteSubUserCommandValidator : AbstractValidator<InviteSubUserCommand>
{
    /// <summary>Longest login name, matching the column and the other places a login is named.</summary>
    private const int MaxUsernameLength = 64;

    /// <summary>Longest address, matching the column.</summary>
    private const int MaxEmailLength = 254;

    /// <summary>What a login name may be made of, the same alphabet every other login uses.</summary>
    private const string UsernamePattern = "^[a-zA-Z0-9._-]+$";

    /// <summary>Configures the field rules for <see cref="InviteSubUserCommand"/>.</summary>
    public InviteSubUserCommandValidator()
    {
        RuleFor(command => command.Username)
            .NotEmpty().WithMessage(nameof(ErrorMessages.UsernameInvalidFormat))
            .MaximumLength(MaxUsernameLength).WithMessage(nameof(ErrorMessages.UsernameInvalidFormat))
            .Matches(UsernamePattern).WithMessage(nameof(ErrorMessages.UsernameInvalidFormat));

        // EmailAddressRule is the panel's one definition of a valid address.
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage(nameof(ErrorMessages.EmailInvalidFormat))
            .MaximumLength(MaxEmailLength).WithMessage(nameof(ErrorMessages.EmailInvalidFormat))
            .Must(EmailAddressRule.IsAddress).WithMessage(nameof(ErrorMessages.EmailInvalidFormat));

        RuleFor(command => command.Features)
            .NotNull().WithMessage(nameof(ErrorMessages.SubUserFeatureInvalid))
            .Must(features =>
            {
                return features is not null && features.All(feature => { return feature.IsFeature(); });
            })
            .WithMessage(nameof(ErrorMessages.SubUserFeatureInvalid));
    }
}
