using FluentValidation;
using Maran.Modules.Firewall.Domain.ValueObjects;
using Maran.Modules.Firewall.Options;
using Maran.Modules.Firewall.Resources;

namespace Maran.Modules.Firewall.Commands.AllowPort;

/// <summary>
/// Validates <see cref="AllowPortCommand"/> before it reaches the handler (rules/security.md
/// "Input"). Every one of these is re-validated inside the agent as well: the API's validation never
/// substitutes for the agent's own boundary check (rules/architecture.md "Agent").
/// </summary>
/// <remarks>
/// The source range is refused when it carries host bits beyond its prefix — <c>203.0.113.7/24</c>
/// is rejected, not masked to <c>203.0.113.0/24</c>. The two readings differ by a factor of two
/// hundred and fifty-six in how much of the internet the rule admits, and silently picking one is
/// how an administrator opens a /24 believing they opened one host. <c>CidrRange</c> refuses it for
/// the same reason, and the agent refuses it again.
///
/// Each message is a bare resx key, not an English sentence. <c>ExceptionMiddleware</c> forwards a
/// validation message only when it is entirely alphanumeric, and then resolves it as an error code
/// against the module's resources; an English sentence is silently discarded and the caller gets the
/// generic failure instead.
/// </remarks>
public sealed class AllowPortCommandValidator : AbstractValidator<AllowPortCommand>
{
    /// <summary>Configures the field rules for <see cref="AllowPortCommand"/>.</summary>
    public AllowPortCommandValidator()
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
