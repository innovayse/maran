using FluentValidation;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Resources;

namespace Maran.Modules.Sites.Commands.ChangeSiteLogRetention;

/// <summary>
/// Validates <see cref="ChangeSiteLogRetentionCommand"/> before it reaches the handler: the number of
/// days must be one a site may keep. The message is a bare resx key, so a sentence in the caller's
/// language is what reaches them.
/// </summary>
public sealed class ChangeSiteLogRetentionCommandValidator : AbstractValidator<ChangeSiteLogRetentionCommand>
{
    /// <summary>Configures the field rules for <see cref="ChangeSiteLogRetentionCommand"/>.</summary>
    public ChangeSiteLogRetentionCommandValidator()
    {
        RuleFor(command => command.SiteId)
            .NotEmpty();

        RuleFor(command => command.Days)
            .Must(SiteLogRetention.IsAllowed)
            .WithMessage(nameof(ErrorMessages.SiteLogRetentionInvalid));
    }
}
