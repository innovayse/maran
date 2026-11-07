using System.Globalization;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>
/// Behavioral contract of <see cref="InvitationMailComposer"/>'s language: the invitation is written
/// in the owner's language, not the language of the request that happens to send it (issue #194).
/// </summary>
public sealed class InvitationMailComposerTests
{
    /// <summary>Builds a composer whose templates report the culture they were read in.</summary>
    private static InvitationMailComposer NewComposer()
    {
        return new InvitationMailComposer(
            new CultureReportingTemplates(),
            new OptionsWrapper<PanelOptions>(new PanelOptions { PublicUrl = "https://panel.example.com" }));
    }

    /// <summary>Runs <paramref name="action"/> with the request culture set to <paramref name="culture"/>.</summary>
    /// <param name="culture">The culture the administrator's request would carry.</param>
    /// <param name="action">The work to run under it.</param>
    private static void UnderRequestCulture(string culture, Action action)
    {
        var previous = CultureInfo.CurrentUICulture;
        var previousFormatting = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            CultureInfo.CurrentCulture = previousFormatting;
        }
    }

    /// <summary>The invitation is written in the owner's language, whatever the request's language is.</summary>
    [Theory]
    [InlineData("en", "hy")]
    [InlineData("ru", "en")]
    [InlineData("hy", "ru")]
    public void The_invitation_is_written_in_the_owners_language_whatever_the_requests_language_is(
        string requestCulture, string ownerLanguage)
    {
        UnderRequestCulture(requestCulture, () =>
        {
            var mail = NewComposer().Compose("owner@example.com", "token", ownerLanguage);

            Assert.StartsWith($"InvitationSubject@{ownerLanguage}:", mail.Subject, StringComparison.Ordinal);
            Assert.StartsWith($"InvitationBody@{ownerLanguage}", mail.Body, StringComparison.Ordinal);
        });
    }

    /// <summary>The request's own language is restored once the invitation is written.</summary>
    [Fact]
    public void The_requests_own_language_is_restored_once_the_invitation_is_written()
    {
        UnderRequestCulture("ru", () =>
        {
            NewComposer().Compose("owner@example.com", "token", "hy");

            Assert.Equal("ru", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("ru", CultureInfo.CurrentCulture.Name);
        });
    }

    /// <summary>A language the panel ships no text in is written in English rather than refused.</summary>
    [Theory]
    [InlineData("de")]
    [InlineData("")]
    public void A_language_the_panel_ships_no_text_in_is_written_in_english_rather_than_refused(string ownerLanguage)
    {
        UnderRequestCulture("ru", () =>
        {
            var mail = NewComposer().Compose("owner@example.com", "token", ownerLanguage);

            Assert.StartsWith("InvitationSubject@en:", mail.Subject, StringComparison.Ordinal);
        });
    }

    /// <summary>The reseller invitation is written in the contact's language, whatever the request's language is.</summary>
    [Theory]
    [InlineData("en", "hy")]
    [InlineData("ru", "en")]
    [InlineData("hy", "ru")]
    public void The_reseller_invitation_is_written_in_the_contacts_language_whatever_the_requests_language_is(
        string requestCulture, string contactLanguage)
    {
        UnderRequestCulture(requestCulture, () =>
        {
            var mail = NewComposer().ComposeForReseller("contact@example.com", "token", contactLanguage);

            Assert.Equal("contact@example.com", mail.Recipient);
            Assert.StartsWith($"ResellerInvitationSubject@{contactLanguage}:", mail.Subject, StringComparison.Ordinal);
            Assert.StartsWith($"ResellerInvitationBody@{contactLanguage}", mail.Body, StringComparison.Ordinal);
        });
    }

    /// <summary>The request's own language is restored once the reseller invitation is written.</summary>
    [Fact]
    public void The_requests_own_language_is_restored_once_the_reseller_invitation_is_written()
    {
        UnderRequestCulture("ru", () =>
        {
            NewComposer().ComposeForReseller("contact@example.com", "token", "hy");

            Assert.Equal("ru", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("ru", CultureInfo.CurrentCulture.Name);
        });
    }

    /// <summary>A language the panel ships no text in is written in English for a reseller too.</summary>
    [Fact]
    public void A_reseller_invitation_in_an_unsupported_language_is_written_in_english()
    {
        UnderRequestCulture("ru", () =>
        {
            var mail = NewComposer().ComposeForReseller("contact@example.com", "token", "de");

            Assert.StartsWith("ResellerInvitationSubject@en:", mail.Subject, StringComparison.Ordinal);
        });
    }

    /// <summary>The reseller invitation uses its own words and the same link as the account invitation.</summary>
    [Fact]
    public void The_reseller_invitation_has_its_own_words_and_the_same_link_as_the_account_invitation()
    {
        var composer = NewComposer();

        var reseller = composer.ComposeForReseller("contact@example.com", "tok en", "en");
        var account = composer.Compose("contact@example.com", "tok en", "en");

        Assert.NotEqual(account.Subject, reseller.Subject);
        Assert.StartsWith("ResellerInvitationBody", reseller.Body, StringComparison.Ordinal);
        Assert.Contains("https://panel.example.com/accept-invitation?token=tok%20en", reseller.Body, StringComparison.Ordinal);
        Assert.Contains("https://panel.example.com/accept-invitation?token=tok%20en", account.Body, StringComparison.Ordinal);
    }

    /// <summary>With no public address configured the reseller mail carries the bare token, never a dead link.</summary>
    [Fact]
    public void A_reseller_invitation_with_no_panel_address_carries_the_bare_token()
    {
        var composer = new InvitationMailComposer(
            new CultureReportingTemplates(),
            new OptionsWrapper<PanelOptions>(new PanelOptions { PublicUrl = string.Empty }));

        var mail = composer.ComposeForReseller("contact@example.com", "the-token", "en");

        Assert.Contains("InvitationTokenOnly@en:the-token", mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("accept-invitation", mail.Body, StringComparison.Ordinal);
    }

    /// <summary>The shipped resources carry a reseller subject and body in every language, each differing from English.</summary>
    /// <remarks>
    /// Real resources, because a missing <c>.ru</c> or <c>.hy</c> entry does not fail a lookup: the
    /// localizer silently falls back to the English text, so only inequality with English proves the
    /// translation exists.
    /// </remarks>
    /// <param name="language">The contact's language.</param>
    [Theory]
    [InlineData("ru")]
    [InlineData("hy")]
    public void The_shipped_reseller_invitation_is_translated(string language)
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            new OptionsWrapper<LocalizationOptions>(new LocalizationOptions()),
            NullLoggerFactory.Instance);
        var composer = new InvitationMailComposer(
            new StringLocalizer<EmailTemplates>(factory),
            new OptionsWrapper<PanelOptions>(new PanelOptions { PublicUrl = "https://panel.example.com" }));

        var english = composer.ComposeForReseller("contact@example.com", "token", "en");
        var translated = composer.ComposeForReseller("contact@example.com", "token", language);

        Assert.NotEqual(english.Subject, translated.Subject);
        Assert.NotEqual(english.Body, translated.Body);
        Assert.Contains("accept-invitation?token=token", translated.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("ResellerInvitation", translated.Subject, StringComparison.Ordinal);
    }

    /// <summary>Template text that names its key and the UI culture it was read in.</summary>
    private sealed class CultureReportingTemplates : IStringLocalizer<EmailTemplates>
    {
        /// <inheritdoc />
        public LocalizedString this[string name]
        {
            get
            {
                return new LocalizedString(name, $"{name}@{CultureInfo.CurrentUICulture.Name}:{{0}}");
            }
        }

        /// <inheritdoc />
        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                return new LocalizedString(
                    name,
                    string.Format(CultureInfo.InvariantCulture, this[name].Value, arguments));
            }
        }

        /// <inheritdoc />
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return [];
        }
    }
}
