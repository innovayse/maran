using FluentValidation;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Commands.DeclineMembershipOffer;

/// <summary>Bounds the fields of <see cref="DeclineMembershipOfferCommand"/>.</summary>
public sealed class DeclineMembershipOfferCommandValidator : AbstractValidator<DeclineMembershipOfferCommand>
{
    /// <summary>Configures the field rules for <see cref="DeclineMembershipOfferCommand"/>.</summary>
    public DeclineMembershipOfferCommandValidator()
    {
        // An empty id names no offer; it is answered as every unknown offer is.
        RuleFor(command => command.OfferId)
            .NotEmpty().WithMessage(nameof(ErrorMessages.MembershipOfferNotFound));
    }
}
