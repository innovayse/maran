using System.Globalization;
using System.Resources;
using Maran.Modules.Identity.Validators;

namespace Maran.Modules.Identity.Tests.Resources;

/// <summary>The password refusals name the operator's minimum, in every language, and not a literal.</summary>
public sealed class PasswordTooWeakTextTests
{
    private static readonly ResourceManager Messages =
        new("Maran.Modules.Identity.Resources.ErrorMessages", typeof(NewPasswordValidator).Assembly);

    /// <summary>PasswordTooWeak carries the length as its {1} placeholder in en, ru and hy.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void PasswordTooWeak_names_the_policy_minimum_it_is_given(string culture)
    {
        var text = Messages.GetString("PasswordTooWeak", CultureInfo.GetCultureInfo(culture));

        Assert.NotNull(text);
        var formatted = string.Format(CultureInfo.InvariantCulture, text, "NewPassword", 16);
        Assert.Contains("16", formatted);
        Assert.DoesNotContain("12", formatted);
    }

    /// <summary>PasswordTooLong exists in en, ru and hy and, unlike PasswordTooWeak, asks for no length.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void PasswordTooLong_has_a_translation_that_asks_for_no_length(string culture)
    {
        var text = Messages.GetString("PasswordTooLong", CultureInfo.GetCultureInfo(culture));

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("{", text);
    }

    /// <summary>PasswordSameAsUsername exists in en, ru and hy and states no length.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    [InlineData("hy")]
    public void PasswordSameAsUsername_has_a_translation_that_states_no_length(string culture)
    {
        var text = Messages.GetString("PasswordSameAsUsername", CultureInfo.GetCultureInfo(culture));

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("{", text);
    }
}
