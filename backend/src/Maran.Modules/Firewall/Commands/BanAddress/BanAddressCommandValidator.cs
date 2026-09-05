using FluentValidation;
using Maran.Modules.Firewall.Resources;

namespace Maran.Modules.Firewall.Commands.BanAddress;

/// <summary>
/// Validates <see cref="BanAddressCommand"/> before it reaches the handler (rules/security.md
/// "Input").
/// </summary>
/// <remarks>
/// The address's FORM is deliberately not checked here. It is parsed once, in the handler, by
/// <c>IpAddressNormalizer</c> — which is also the thing that maps <c>::ffff:a.b.c.d</c> onto plain
/// IPv4 — and a second format rule here would be a check that masks the one doing the work: a
/// mutation removing the normalisation would leave this validator still passing every address, so
/// nothing would go red. One check, in the place that has to succeed for the ban to mean anything.
///
/// The duration IS checked here, because the handler cannot: a value of zero minutes is a
/// well-formed request for something the contract spells as "permanent", so it has to be refused
/// before it becomes a ban nobody expected to be permanent.
/// </remarks>
public sealed class BanAddressCommandValidator : AbstractValidator<BanAddressCommand>
{
    /// <summary>The longest ban a duration may ask for: a year, in minutes.</summary>
    /// <remarks>
    /// Not a technical limit — the wire carries far more — but the point past which "temporary" has
    /// stopped meaning anything. An administrator who wants longer wants a permanent ban, and says
    /// so by sending no duration at all, which is a decision the journal then records as what it is.
    /// </remarks>
    private const int MaximumDurationMinutes = 525_600;

    /// <summary>Configures the field rules for <see cref="BanAddressCommand"/>.</summary>
    public BanAddressCommandValidator()
    {
        RuleFor(command => command.Address)
            .NotEmpty();

        RuleFor(command => command.DurationMinutes!.Value)
            .InclusiveBetween(1, MaximumDurationMinutes)
            .When(command =>
            {
                return command.DurationMinutes is not null;
            })
            .WithMessage(nameof(ErrorMessages.BanDurationInvalid));
    }
}
