using FluentValidation;

namespace Maran.Modules.Identity.Commands.IssueInvitationLink;

/// <summary>Bounds the fields of <see cref="IssueInvitationLinkCommand"/>.</summary>
public sealed class IssueInvitationLinkCommandValidator : AbstractValidator<IssueInvitationLinkCommand>
{
    /// <summary>Configures the field rules for <see cref="IssueInvitationLinkCommand"/>.</summary>
    public IssueInvitationLinkCommandValidator()
    {
        RuleFor(command => command.AccountId)
            .NotEmpty().WithMessage(nameof(Resources.ErrorMessages.InvitationAccountNotFound));
    }
}
