using FluentValidation;

namespace Maran.Modules.Identity.Queries.GetInvitationStatus;

/// <summary>Bounds the fields of <see cref="GetInvitationStatusQuery"/>.</summary>
public sealed class GetInvitationStatusQueryValidator : AbstractValidator<GetInvitationStatusQuery>
{
    /// <summary>Configures the field rules for <see cref="GetInvitationStatusQuery"/>.</summary>
    public GetInvitationStatusQueryValidator()
    {
        RuleFor(query => query.AccountId)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.InvitationAccountNotFound));
    }
}
