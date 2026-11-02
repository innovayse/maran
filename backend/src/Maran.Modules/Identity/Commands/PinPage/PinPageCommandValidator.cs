using FluentValidation;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.PinPage;

/// <summary>
/// Holds <see cref="PinPageCommand.ItemKey"/> to the shape of a navigation tree key
/// (rules/security.md "Input"). The three server-established members are not validated here: the
/// action writes them from the caller's token and connection.
/// </summary>
public sealed class PinPageCommandValidator : AbstractValidator<PinPageCommand>
{
    /// <summary>Declares the key rule.</summary>
    public PinPageCommandValidator()
    {
        RuleFor(command => command.ItemKey)
            .Must(PinnedPagePolicy.IsValidItemKey).WithMessage(nameof(ErrorMessages.PinnedPageKeyInvalid));
    }
}
