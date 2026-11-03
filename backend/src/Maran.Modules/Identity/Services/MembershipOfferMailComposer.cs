using System.Globalization;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Utilities.Mail;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Renders the plain notification sent to an existing login that an account's owner has offered a
/// place on the account's team.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mail carries nothing that acts as a credential.</b> No token, no link secret, no address
/// the person must open: it names the account and says to sign in to the panel and review the offer.
/// Consent happens inside an authenticated session, so intercepting the mail yields a sentence and
/// nothing to use. It also carries no address of the owner.
/// </para>
/// <para>
/// <b>The account's name is written into the body only, and only when it is safe to.</b> A name with a
/// control character (a carriage return or line feed) is replaced by the generic wording rather than
/// written, as <see cref="MailHeaderTextRule"/> asks of anything that reaches a mail, and the render
/// never refuses: a refusal would be seen only on the path that stored an offer, which is the
/// difference the request is built not to show.
/// </para>
/// <para>
/// <b>The language is the account owner's</b> (<c>OwnerLanguage</c>): the offered login has no stored
/// language preference of its own, and the owner's is the one the panel already writes the account's
/// mail in.
/// </para>
/// </remarks>
public sealed class MembershipOfferMailComposer
{
    /// <summary>The message text, in the recipient's language.</summary>
    private readonly IStringLocalizer<EmailTemplates> _templates;

    /// <summary>Creates the composer.</summary>
    /// <param name="templates">The message text, in the recipient's language.</param>
    public MembershipOfferMailComposer(IStringLocalizer<EmailTemplates> templates)
    {
        _templates = templates;
    }

    /// <summary>Renders the notification, ready to hand to the panel's mail queue.</summary>
    /// <param name="recipient">The offered login's own stored address, never text the owner typed.</param>
    /// <param name="accountName">The name of the account making the offer.</param>
    /// <param name="language">The language to write it in, one of <see cref="PanelLanguages.Supported"/>.</param>
    /// <returns>The message to publish as <see cref="SendMailRequested"/>.</returns>
    public SendMailRequested Compose(string recipient, string accountName, string language)
    {
        using var scope = new RecipientCultureScope(language);

        var account = !string.IsNullOrWhiteSpace(accountName) && MailHeaderTextRule.IsHeaderSafe(accountName)
            ? string.Format(CultureInfo.CurrentCulture, _templates["MembershipOfferAccountNamed"], accountName)
            : _templates["MembershipOfferAccountUnnamed"].Value;
        var body = string.Format(CultureInfo.CurrentCulture, _templates["MembershipOfferBody"], account);

        return new SendMailRequested(recipient, _templates["MembershipOfferSubject"], body);
    }
}
