using System.Globalization;
using Maran.Modules.Identity.Options;
using Maran.Modules.Identity.Resources;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Renders the invitation mail sent to a hosting account's owner once their panel login exists.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>RequestPasswordResetCommandHandler.Compose</c> — read that type's remarks first. The
/// link is built from <see cref="PanelOptions.PanelUrl"/>, the panel's own configured public
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

    /// <summary>Renders the invitation message, already localized, ready to hand to the panel's mail queue.</summary>
    /// <param name="recipient">The invited user's own stored address.</param>
    /// <param name="token">The plaintext invitation token. It exists here, in the message, and nowhere else.</param>
    /// <returns>The message to publish as <see cref="SendMailRequested"/>.</returns>
    public SendMailRequested Compose(string recipient, string token)
    {
        var instruction = string.IsNullOrWhiteSpace(_options.PanelUrl)
            ? string.Format(CultureInfo.CurrentCulture, _templates["InvitationTokenOnly"], token)
            : string.Format(
                CultureInfo.CurrentCulture,
                _templates["InvitationLink"],
                $"{_options.PanelUrl.TrimEnd('/')}/accept-invitation?token={Uri.EscapeDataString(token)}");

        var body = string.Format(CultureInfo.CurrentCulture, _templates["InvitationBody"], instruction);

        return new SendMailRequested(recipient, _templates["InvitationSubject"], body);
    }
}
