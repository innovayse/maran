using FluentValidation;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.UnpinPage;

/// <summary>
/// Holds the route's key to the shape of a navigation tree key, so a malformed one is a 400 and not
/// a query for a row that cannot exist (rules/security.md "Input").
/// </summary>
public sealed class UnpinPageCommandValidator : AbstractValidator<UnpinPageCommand>
{
    /// <summary>Declares the key rule.</summary>
    public UnpinPageCommandValidator()
    {
        RuleFor(command => command.ItemKey)
            .Must(PinnedPagePolicy.IsValidItemKey).WithMessage(nameof(ErrorMessages.PinnedPageKeyInvalid));
    }
}
