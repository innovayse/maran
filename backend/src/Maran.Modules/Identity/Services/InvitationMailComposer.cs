using System.Globalization;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Constants;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Renders the invitation mail sent to a hosting account's owner once their panel login exists.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>RequestPasswordResetCommandHandler.Compose</c> — read that type's remarks first. The
/// link is built from <see cref="PanelOptions.PublicUrl"/>, joined by <see cref="PanelLink"/>, the panel's own configured public
/// address rather than anything a caller supplied, for the identical host-header-injection reason
/// stated there; and when no public address is configured, the mail falls back to the bare token
/// instead of a link nobody could open.
/// </para>
/// <para>
/// It is a standalone service rather than a private method on one handler because two handlers need
/// it: <see cref="Maran.Modules.Identity.IntegrationEvents.Handlers.AccountCreatedHandler"/> uses it
/// when a hosting account is first created, and the "resend invitation" handler reuses it verbatim
/// for the same account later. A shared facility used by two handlers inside one module is a
/// service, not a copy kept in sync by hand.
/// </para>
/// </remarks>
public sealed class InvitationMailComposer
{
    /// <summary>The invitation message text, in the recipient's language.</summary>
    private readonly IStringLocalizer<EmailTemplates> _templates;

    /// <summary>The panel's own public address, for the link in the mail.</summary>
    private readonly PanelOptions _options;

    /// <summary>Creates the composer.</summary>
    /// <param name="templates">The invitation message text, in the recipient's language.</param>
    /// <param name="options">The panel's own public address.</param>
    public InvitationMailComposer(IStringLocalizer<EmailTemplates> templates, IOptions<PanelOptions> options)
    {
        _templates = templates;
        _options = options.Value;
    }

    /// <summary>Renders the invitation message in the owner's language, ready to hand to the panel's mail queue.</summary>
    /// <remarks>
    /// The language is the OWNER's, never the request's: an invitation is written while an
    /// administrator's request is being served, and that request's culture says what the
    /// administrator reads, not what the owner does (issue #194). Both cultures are switched for the
    /// duration of this synchronous call and restored before it returns, so nothing after it in the
    /// administrator's request is answered in the owner's language. An unsupported value — a row
    /// written before the language was validated — is rendered in English rather than refused,
    /// because the mail is a recovery path and must not fail on a stored preference.
    /// </remarks>
    /// <param name="recipient">The invited user's own stored address.</param>
    /// <param name="token">The plaintext invitation token. It exists here, in the message, and nowhere else.</param>
    /// <param name="language">The owner's language, one of <see cref="PanelLanguages.Supported"/>.</param>
    /// <returns>The message to publish as <see cref="SendMailRequested"/>.</returns>
    public SendMailRequested Compose(string recipient, string token, string language)
    {
        using var scope = new RecipientCultureScope(language);
        return Render(recipient, token, "InvitationSubject", "InvitationBody");
    }

    /// <summary>
    /// Renders the invitation mail sent to a reseller's contact once the reseller's login exists, in
    /// the contact's language. Same link, same token handling and same culture rules as
    /// <see cref="Compose"/>; only the words differ, because what the mail announces is a reseller
    /// login and not a hosting account.
    /// </summary>
    /// <param name="recipient">The invited login's own stored address.</param>
    /// <param name="token">The plaintext invitation token. It exists here, in the message, and nowhere else.</param>
    /// <param name="language">The contact's language, one of <see cref="PanelLanguages.Supported"/>.</param>
    /// <returns>The message to publish as <see cref="SendMailRequested"/>.</returns>
    public SendMailRequested ComposeForReseller(string recipient, string token, string language)
    {
        using var scope = new RecipientCultureScope(language);
        return Render(recipient, token, "ResellerInvitationSubject", "ResellerInvitationBody");
    }

    /// <summary>Renders the message in whatever culture is current.</summary>
    /// <param name="recipient">The invited user's own stored address.</param>
    /// <param name="token">The plaintext invitation token.</param>
    /// <param name="subjectKey">The resource key of the subject line.</param>
    /// <param name="bodyKey">The resource key of the body, which has one placeholder for the instruction.</param>
    /// <returns>The message to publish as <see cref="SendMailRequested"/>.</returns>
    private SendMailRequested Render(string recipient, string token, string subjectKey, string bodyKey)
    {
        var instruction = !PanelLink.IsConfigured(_options.PublicUrl)
            ? string.Format(CultureInfo.CurrentCulture, _templates["InvitationTokenOnly"], token)
            : string.Format(
                CultureInfo.CurrentCulture,
                _templates["InvitationLink"],
                PanelLink.Compose(_options.PublicUrl, InvitationLinkBuilder.RelativeFor(token)));

        var body = string.Format(CultureInfo.CurrentCulture, _templates[bodyKey], instruction);

        return new SendMailRequested(recipient, _templates[subjectKey], body);
    }
}
