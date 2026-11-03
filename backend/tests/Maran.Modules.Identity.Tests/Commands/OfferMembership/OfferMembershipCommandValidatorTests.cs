using Maran.Modules.Identity.Commands.OfferMembership;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Commands.OfferMembership;

/// <summary>Behavioural contract of the offer command's input rules.</summary>
public sealed class OfferMembershipCommandValidatorTests
{
    private static OfferMembershipCommand Command(string identifier, params AccountFeature[] features)
    {
        return new OfferMembershipCommand(identifier, features);
    }

    /// <summary>A login name or an address is accepted.</summary>
    [Theory]
    [InlineData("deploy")]
    [InlineData("a.b_c-d")]
    [InlineData("deploy@example.com")]
    public void A_login_name_or_an_address_is_accepted(string identifier)
    {
        Assert.True(new OfferMembershipCommandValidator().Validate(Command(identifier)).IsValid);
    }

    /// <summary>Text that is neither a login name nor an address is refused with the code of the rule it broke.</summary>
    [Theory]
    [InlineData("", "UsernameInvalidFormat")]
    [InlineData("two words", "UsernameInvalidFormat")]
    [InlineData("name\n", "UsernameInvalidFormat")]
    [InlineData("not an@address", "EmailInvalidFormat")]
    public void Text_that_is_neither_is_refused_with_the_rule_it_broke(string identifier, string code)
    {
        var result = new OfferMembershipCommandValidator().Validate(Command(identifier));

        Assert.Contains(result.Errors, failure => { return failure.ErrorMessage == code; });
    }

    /// <summary>A combined or unknown feature value is refused.</summary>
    [Fact]
    public void A_combined_feature_is_refused()
    {
        var result = new OfferMembershipCommandValidator().Validate(Command("deploy", AccountFeature.All));

        Assert.Contains(result.Errors, failure => { return failure.ErrorMessage == "SubUserFeatureInvalid"; });
    }
}
