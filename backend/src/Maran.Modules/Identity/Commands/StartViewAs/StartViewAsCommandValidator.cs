using FluentValidation;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.StartViewAs;

/// <summary>Bounds <see cref="StartViewAsCommand"/>: an account must be named.</summary>
/// <remarks>
/// Whether the account EXISTS, and whether the caller may see it, is the handler's question, answered
/// through the tenant-scoped account directory; asking it here would split one 404 across two places.
/// </remarks>
public sealed class StartViewAsCommandValidator : AbstractValidator<StartViewAsCommand>
{
    /// <summary>Configures the field rules for <see cref="StartViewAsCommand"/>.</summary>
    public StartViewAsCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty().WithMessage(nameof(ErrorMessages.ViewAsAccountNotFound));
    }
}
