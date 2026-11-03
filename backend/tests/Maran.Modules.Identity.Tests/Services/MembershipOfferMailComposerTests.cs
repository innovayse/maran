using System.Globalization;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of <see cref="MembershipOfferMailComposer"/>.</summary>
public sealed class MembershipOfferMailComposerTests
{
    /// <summary>Builds a composer over the shipped templates, so the real text is what is read.</summary>
    private static MembershipOfferMailComposer NewComposer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);

        return new MembershipOfferMailComposer(new StringLocalizer<EmailTemplates>(factory));
    }

    /// <summary>The mail is addressed to the recipient given, names the account, and carries no link or token.</summary>
    [Fact]
    public void The_mail_names_the_account_and_carries_no_link_or_token()
    {
        var mail = NewComposer().Compose("bob@example.com", "acme", "en");

        Assert.Equal("bob@example.com", mail.Recipient);
        Assert.Contains("acme", mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("http", mail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", mail.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("code", mail.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The mail is written in the language asked for, and the request's own culture is put back.</summary>
    [Theory]
    [InlineData("ru")]
    [InlineData("hy")]
    public void The_mail_is_written_in_the_language_asked_for_and_the_request_culture_is_restored(string language)
    {
        var before = CultureInfo.CurrentUICulture;

        var mail = NewComposer().Compose("bob@example.com", "acme", language);
        var english = NewComposer().Compose("bob@example.com", "acme", "en");

        Assert.NotEqual(english.Subject, mail.Subject);
        Assert.Equal(before, CultureInfo.CurrentUICulture);
    }

    /// <summary>An account name with a control character is never written into the mail.</summary>
    [Theory]
    [InlineData("acme\r\nBcc: someone@example.org")]
    [InlineData("ac\nme")]
    [InlineData("")]
    public void An_account_name_that_is_not_safe_to_write_is_replaced_by_the_generic_wording(string name)
    {
        var mail = NewComposer().Compose("bob@example.com", name, "en");

        Assert.DoesNotContain("Bcc", mail.Body, StringComparison.Ordinal);
        Assert.Contains("a hosting account", mail.Body, StringComparison.Ordinal);
    }
}
