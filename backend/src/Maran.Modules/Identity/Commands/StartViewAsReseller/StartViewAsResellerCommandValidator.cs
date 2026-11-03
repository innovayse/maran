using FluentValidation;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.StartViewAsReseller;

/// <summary>Bounds <see cref="StartViewAsResellerCommand"/>: a reseller must be named.</summary>
/// <remarks>
/// Whether the reseller EXISTS is the handler's question, answered through the reseller directory;
/// asking it here would split one 404 across two places.
/// </remarks>
public sealed class StartViewAsResellerCommandValidator : AbstractValidator<StartViewAsResellerCommand>
{
    /// <summary>Configures the field rules for <see cref="StartViewAsResellerCommand"/>.</summary>
    public StartViewAsResellerCommandValidator()
    {
        RuleFor(command => command.ResellerId)
            .NotEmpty().WithMessage(nameof(ErrorMessages.ViewAsResellerNotFound));
    }
}
