using Maran.Modules.Identity.Commands.PinPage;

namespace Maran.Modules.Identity.Tests.Commands.PinPage;

/// <summary>Behavioural contract of <see cref="PinPageCommandValidator"/>.</summary>
public sealed class PinPageCommandValidatorTests
{
    /// <summary>A tree-shaped key passes.</summary>
    [Fact]
    public void A_navigation_key_passes()
    {
        Assert.True(new PinPageCommandValidator().Validate(new PinPageCommand("accounts-list")).IsValid);
    }

    /// <summary>A key with a newline or a capital fails with the resource key as its message.</summary>
    [Theory]
    [InlineData("Accounts")]
    [InlineData("a\nb")]
    [InlineData("")]
    public void A_key_outside_the_charset_fails_with_its_resource_key(string key)
    {
        var result = new PinPageCommandValidator().Validate(new PinPageCommand(key));

        Assert.Equal("PinnedPageKeyInvalid", Assert.Single(result.Errors).ErrorMessage);
    }
}
