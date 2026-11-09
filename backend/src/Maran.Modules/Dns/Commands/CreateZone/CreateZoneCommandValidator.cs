using FluentValidation;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Resources;

namespace Maran.Modules.Dns.Commands.CreateZone;

/// <summary>
/// Validates <see cref="CreateZoneCommand"/> before it reaches the handler (rules/security.md "Input"). The
/// name is judged by <see cref="ZoneNamePolicy"/>, the same rule the zone itself insists on.
/// </summary>
/// <remarks>
/// The message is a bare resx key, not an English sentence: the exception middleware forwards a validation
/// message only when it is entirely alphanumeric, and then resolves it as an error code.
/// </remarks>
public sealed class CreateZoneCommandValidator : AbstractValidator<CreateZoneCommand>
{
    /// <summary>Configures the field rules for <see cref="CreateZoneCommand"/>.</summary>
    public CreateZoneCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty();

        RuleFor(command => command.Name)
            .NotEmpty()
            .WithMessage(nameof(ErrorMessages.ZoneNameInvalid))
            .Must(name =>
            {
                return ZoneNamePolicy.Check(ZoneNamePolicy.Normalize(name)) == ZoneNameVerdict.Accepted;
            })
            .WithMessage(nameof(ErrorMessages.ZoneNameInvalid));
    }
}
