using FluentValidation;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Resources;

namespace Maran.Modules.Firewall.Commands.DenyPort;

/// <summary>
/// Validates <see cref="DenyPortCommand"/> before it reaches the handler (rules/security.md
/// "Input"), on the same terms as the matching allow.
/// </summary>
/// <remarks>
/// The same rules as <c>AllowPortCommandValidator</c>, and deliberately not looser. A deny is the
/// call that removes a rule, so a source range accepted here in a spelling the allow would have
/// refused is a deny that matches nothing and reports success — the administrator then believes a
/// port is closed while it is open.
/// </remarks>
public sealed class DenyPortCommandValidator : AbstractValidator<DenyPortCommand>
{
    /// <summary>Configures the field rules for <see cref="DenyPortCommand"/>.</summary>
    public DenyPortCommandValidator()
    {
        RuleFor(command => command.Port)
            .Must(FirewallOptions.IsUsablePort)
            .WithMessage(nameof(ErrorMessages.RulePortInvalid));

        RuleFor(command => command.Protocol)
            .IsInEnum()
            .WithMessage(nameof(ErrorMessages.RuleProtocolInvalid));

        RuleFor(command => command.SourceCidr)
            .NotEmpty()
            .Must(CidrRange.IsUsable)
            .WithMessage(nameof(ErrorMessages.RuleSourceCidrInvalid));
    }
}
