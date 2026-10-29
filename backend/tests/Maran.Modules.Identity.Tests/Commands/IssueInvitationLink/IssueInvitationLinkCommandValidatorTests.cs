using FluentValidation.TestHelper;
using Maran.Modules.Identity.Commands.IssueInvitationLink;

namespace Maran.Modules.Identity.Tests.Commands.IssueInvitationLink;

/// <summary>Behavioral contract of <see cref="IssueInvitationLinkCommandValidator"/>.</summary>
public sealed class IssueInvitationLinkCommandValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly IssueInvitationLinkCommandValidator _validator = new();

    /// <summary>A request naming an account passes.</summary>
    [Fact]
    public async Task A_request_naming_an_account_passes()
    {
        var result = await _validator.TestValidateAsync(new IssueInvitationLinkCommand(Guid.NewGuid()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A request naming no account is refused.</summary>
    [Fact]
    public async Task A_request_naming_no_account_is_refused()
    {
        var result = await _validator.TestValidateAsync(new IssueInvitationLinkCommand(Guid.Empty));

        result.ShouldHaveValidationErrorFor(command => command.AccountId);
    }
}
