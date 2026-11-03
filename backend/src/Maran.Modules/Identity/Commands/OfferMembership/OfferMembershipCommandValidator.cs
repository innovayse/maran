using System.Text.RegularExpressions;
using FluentValidation;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Extensions;
using Maran.SharedKernel.Utilities.Mail;

namespace Maran.Modules.Identity.Commands.OfferMembership;

/// <summary>Bounds the fields of <see cref="OfferMembershipCommand"/>.</summary>
public sealed class OfferMembershipCommandValidator : AbstractValidator<OfferMembershipCommand>
{
    /// <summary>Longest address the panel accepts. A login name is shorter and bounded by <see cref="UsernamePattern"/>.</summary>
    private const int MaxIdentifierLength = 254;

    /// <summary>
    /// What a login name may be made of, the same alphabet every other login uses. Anchored with
    /// <c>\z</c>, not <c>$</c>, which would also accept a name followed by a line feed.
    /// </summary>
    private static readonly Regex UsernamePattern = new(
        "^[a-zA-Z0-9._-]{1,64}\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Configures the field rules for <see cref="OfferMembershipCommand"/>.</summary>
    public OfferMembershipCommandValidator()
    {
        // One field, two shapes, told apart by the "@" that no login name can contain. Each shape is
        // held to the rule the panel applies when it creates such a login, so nothing is accepted
        // here that no login could hold, and the refusal names the rule the person broke.
        When(command => { return command.Identifier is not null && command.Identifier.Contains('@'); }, () =>
        {
            RuleFor(command => command.Identifier)
                .MaximumLength(MaxIdentifierLength).WithMessage(nameof(ErrorMessages.EmailInvalidFormat))
                .Must(EmailAddressRule.IsAddress).WithMessage(nameof(ErrorMessages.EmailInvalidFormat));
        }).Otherwise(() =>
        {
            RuleFor(command => command.Identifier)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage(nameof(ErrorMessages.UsernameInvalidFormat))
                .Must(identifier => { return UsernamePattern.IsMatch(identifier); })
                .WithMessage(nameof(ErrorMessages.UsernameInvalidFormat));
        });

        RuleFor(command => command.Features)
            .NotNull().WithMessage(nameof(ErrorMessages.SubUserFeatureInvalid))
            .Must(features =>
            {
                return features is not null && features.All(feature => { return feature.IsFeature(); });
            })
            .WithMessage(nameof(ErrorMessages.SubUserFeatureInvalid));
    }
}
