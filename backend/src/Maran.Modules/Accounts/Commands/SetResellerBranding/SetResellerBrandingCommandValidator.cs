using FluentValidation;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Domain.Policies;
using Maran.Modules.Accounts.Resources;

namespace Maran.Modules.Accounts.Commands.SetResellerBranding;

/// <summary>Validates <see cref="SetResellerBrandingCommand"/> before it reaches the handler (rules/security.md "Input").</summary>
public sealed class SetResellerBrandingCommandValidator : AbstractValidator<SetResellerBrandingCommand>
{
    /// <summary>Configures the rules: a name inside its length with no control characters, a readable colour, an image logo.</summary>
    public SetResellerBrandingCommandValidator()
    {
        // Codes are passed as the message: the exception middleware resolves an entirely alphanumeric
        // message as a resx key (see ResellerEnvelopeDtoValidator).
        RuleFor(command => command.DisplayName)
            .Must(name =>
            {
                return name is null
                    || (name.Trim().Length <= ResellerBranding.DisplayNameMaxLength && !name.Any(char.IsControl));
            })
            .WithMessage(nameof(ErrorMessages.BrandingDisplayNameInvalid));

        RuleFor(command => command.AccentColor)
            .Cascade(CascadeMode.Stop)
            .Must(color => { return color is null || BrandingAccentPolicy.Check(color) != BrandingAccentVerdict.NotAColor; })
            .WithMessage(nameof(ErrorMessages.BrandingAccentColorInvalid))
            .Must(color => { return color is null || BrandingAccentPolicy.Check(color) == BrandingAccentVerdict.Acceptable; })
            .WithMessage(nameof(ErrorMessages.BrandingAccentColorLowContrast));

        RuleFor(command => command.Logo)
            .Cascade(CascadeMode.Stop)
            .Must(logo => { return logo is null || BrandingLogoPolicy.Check(logo) != BrandingLogoVerdict.NotAnImage; })
            .WithMessage(nameof(ErrorMessages.BrandingLogoInvalid))
            .Must(logo => { return logo is null || BrandingLogoPolicy.Check(logo) == BrandingLogoVerdict.Acceptable; })
            .WithMessage(nameof(ErrorMessages.BrandingLogoTooLarge));
    }
}
