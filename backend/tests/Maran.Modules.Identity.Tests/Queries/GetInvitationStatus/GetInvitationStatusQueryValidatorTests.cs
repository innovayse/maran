using FluentValidation.TestHelper;
using Maran.Modules.Identity.Queries.GetInvitationStatus;

namespace Maran.Modules.Identity.Tests.Queries.GetInvitationStatus;

/// <summary>Behavioral contract of <see cref="GetInvitationStatusQueryValidator"/>.</summary>
public sealed class GetInvitationStatusQueryValidatorTests
{
    /// <summary>The validator under test.</summary>
    private readonly GetInvitationStatusQueryValidator _validator = new();

    /// <summary>A query naming an account passes.</summary>
    [Fact]
    public async Task A_query_naming_an_account_passes()
    {
        var result = await _validator.TestValidateAsync(new GetInvitationStatusQuery(Guid.NewGuid()));

        result.ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>A query naming no account is refused.</summary>
    [Fact]
    public async Task A_query_naming_no_account_is_refused()
    {
        var result = await _validator.TestValidateAsync(new GetInvitationStatusQuery(Guid.Empty));

        result.ShouldHaveValidationErrorFor(query => query.AccountId);
    }
}
