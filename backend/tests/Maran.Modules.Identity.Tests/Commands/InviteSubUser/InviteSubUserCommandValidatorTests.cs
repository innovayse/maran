using Maran.Modules.Identity.Commands.InviteSubUser;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Tests.Commands.InviteSubUser;

/// <summary>Behavioural contract of the sub-user invitation validator.</summary>
public sealed class InviteSubUserCommandValidatorTests
{
    private static InviteSubUserCommand Command(
        string username = "deploy",
        string email = "deploy@example.com",
        IReadOnlyList<AccountFeature>? features = null)
    {
        return new InviteSubUserCommand(username, email, features ?? [AccountFeature.Databases]);
    }

    /// <summary>A well-formed request passes; the control for every refusal below.</summary>
    [Fact]
    public void A_well_formed_request_is_accepted()
    {
        Assert.True(new InviteSubUserCommandValidator().Validate(Command()).IsValid);
    }

    /// <summary>An empty feature list is a legitimate choice: the person reaches nothing gated.</summary>
    [Fact]
    public void An_empty_feature_list_is_accepted()
    {
        Assert.True(new InviteSubUserCommandValidator().Validate(Command(features: [])).IsValid);
    }

    /// <summary>A login name with a space or a slash is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("two words")]
    [InlineData("a/b")]
    public void A_malformed_login_name_is_refused(string username)
    {
        var result = new InviteSubUserCommandValidator().Validate(Command(username: username));

        Assert.Contains(result.Errors, error => { return error.ErrorMessage == "UsernameInvalidFormat"; });
    }

    /// <summary>An address that is not a bare e-mail address is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Ops Team <ops@example.com>")]
    [InlineData("nobody")]
    public void A_malformed_address_is_refused(string email)
    {
        var result = new InviteSubUserCommandValidator().Validate(Command(email: email));

        Assert.Contains(result.Errors, error => { return error.ErrorMessage == "EmailInvalidFormat"; });
    }

    /// <summary>A combined set, the empty marker, or an undefined bit is not a single feature.</summary>
    [Theory]
    [InlineData(AccountFeature.None)]
    [InlineData(AccountFeature.All)]
    [InlineData(AccountFeature.Mail | AccountFeature.Dns)]
    [InlineData((AccountFeature)(1L << 40))]
    public void Anything_that_is_not_one_feature_is_refused(AccountFeature feature)
    {
        var result = new InviteSubUserCommandValidator().Validate(Command(features: [feature]));

        Assert.Contains(result.Errors, error => { return error.ErrorMessage == "SubUserFeatureInvalid"; });
    }
}
