using FluentValidation;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Identity.Commands.ChangeSubUserGrants;

/// <summary>Bounds the fields of <see cref="ChangeSubUserGrantsCommand"/>.</summary>
public sealed class ChangeSubUserGrantsCommandValidator : AbstractValidator<ChangeSubUserGrantsCommand>
{
    /// <summary>Configures the field rules for <see cref="ChangeSubUserGrantsCommand"/>.</summary>
    public ChangeSubUserGrantsCommandValidator()
    {
        RuleFor(command => command.Features)
            .NotNull().WithMessage(nameof(ErrorMessages.SubUserFeatureInvalid))
            .Must(features =>
            {
                return features is not null && features.All(feature => { return feature.IsFeature(); });
            })
            .WithMessage(nameof(ErrorMessages.SubUserFeatureInvalid));
    }
}
