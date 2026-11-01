using System.Globalization;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Microsoft.Extensions.Localization;
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
            new OptionsWrapper<PanelOptions>(new PanelOptions { PanelUrl = "https://panel.example.com" }));
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
