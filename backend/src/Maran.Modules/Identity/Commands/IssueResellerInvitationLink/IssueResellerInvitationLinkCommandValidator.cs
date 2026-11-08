using FluentValidation;

namespace Maran.Modules.Identity.Commands.IssueResellerInvitationLink;

/// <summary>Bounds the fields of <see cref="IssueResellerInvitationLinkCommand"/>.</summary>
public sealed class IssueResellerInvitationLinkCommandValidator : AbstractValidator<IssueResellerInvitationLinkCommand>
{
    /// <summary>Configures the field rules for <see cref="IssueResellerInvitationLinkCommand"/>.</summary>
    public IssueResellerInvitationLinkCommandValidator()
    {
        RuleFor(command => command.ResellerId)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.InvitationResellerNotFound));
    }
}
