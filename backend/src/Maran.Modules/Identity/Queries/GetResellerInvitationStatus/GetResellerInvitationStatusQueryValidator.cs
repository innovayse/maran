using FluentValidation;

namespace Maran.Modules.Identity.Queries.GetResellerInvitationStatus;

/// <summary>Bounds the fields of <see cref="GetResellerInvitationStatusQuery"/>.</summary>
public sealed class GetResellerInvitationStatusQueryValidator : AbstractValidator<GetResellerInvitationStatusQuery>
{
    /// <summary>Configures the field rules for <see cref="GetResellerInvitationStatusQuery"/>.</summary>
    public GetResellerInvitationStatusQueryValidator()
    {
        RuleFor(query => query.ResellerId)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.InvitationResellerNotFound));
    }
}
