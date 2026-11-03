using FluentValidation;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.AcceptMembershipOffer;

/// <summary>Bounds the fields of <see cref="AcceptMembershipOfferCommand"/>.</summary>
public sealed class AcceptMembershipOfferCommandValidator : AbstractValidator<AcceptMembershipOfferCommand>
{
    /// <summary>Configures the field rules for <see cref="AcceptMembershipOfferCommand"/>.</summary>
    public AcceptMembershipOfferCommandValidator()
    {
        // An empty id names no offer; it is answered as every unknown offer is.
        RuleFor(command => command.OfferId)
            .NotEmpty().WithMessage(nameof(ErrorMessages.MembershipOfferNotFound));
    }
}
