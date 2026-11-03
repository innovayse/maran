using System.Globalization;
using Maran.SharedKernel.Constants;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Makes the current culture the RECIPIENT's for as long as the scope is held, and puts the request's
/// own back when it is disposed.
/// </summary>
/// <remarks>
/// A mail is written while somebody else's request is being served, and that request's culture says
/// what the requester reads, not what the recipient does (issue #194). Both cultures are switched, and
/// both are restored whatever happens in between, so nothing after the render in the requester's
/// request is answered in the recipient's language. An unsupported value, such as a row written before
/// the language was validated, is rendered in English rather than refused: a mail must not fail on a
/// stored preference.
/// </remarks>
public sealed class RecipientCultureScope : IDisposable
{
    /// <summary>The culture the request was using, restored on disposal.</summary>
    private readonly CultureInfo _requestCulture;

    /// <summary>The UI culture the request was using, restored on disposal.</summary>
    private readonly CultureInfo _requestUiCulture;

    /// <summary>Switches the current culture to the recipient's.</summary>
    /// <param name="language">The recipient's language, one of <see cref="PanelLanguages.Supported"/>.</param>
    public RecipientCultureScope(string language)
    {
        _requestCulture = CultureInfo.CurrentCulture;
        _requestUiCulture = CultureInfo.CurrentUICulture;

        var recipientCulture = CultureInfo.GetCultureInfo(
            PanelLanguages.IsSupported(language) ? language : PanelLanguages.English);
        CultureInfo.CurrentCulture = recipientCulture;
        CultureInfo.CurrentUICulture = recipientCulture;
    }

    /// <summary>Restores the request's cultures.</summary>
    public void Dispose()
    {
        CultureInfo.CurrentCulture = _requestCulture;
        CultureInfo.CurrentUICulture = _requestUiCulture;
    }
}
