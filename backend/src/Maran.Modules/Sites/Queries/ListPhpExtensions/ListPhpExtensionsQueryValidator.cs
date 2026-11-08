using FluentValidation;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Resources;

namespace Maran.Modules.Sites.Queries.ListPhpExtensions;

/// <summary>Refuses anything that is not a PHP version number before the agent is asked.</summary>
/// <remarks>
/// The same pattern every other validator in the module uses. Whether the host has the version is
/// answered by the agent, which also validates it again.
/// </remarks>
public sealed class ListPhpExtensionsQueryValidator : AbstractValidator<ListPhpExtensionsQuery>
{
    /// <summary>Creates the validator.</summary>
    public ListPhpExtensionsQueryValidator()
    {
        RuleFor(query => query.Version)
            .NotEmpty()
            .Matches(PhpVersionFormat.Pattern)
            .WithMessage(nameof(ErrorMessages.PhpVersionInvalidFormat));
    }
}
