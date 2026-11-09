using Maran.Modules.Mail.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// A display-name localizer that answers each key with the key itself, so a mailbox test can assert
/// exactly which resource a handler asked for without depending on the wording of any language.
/// </summary>
public sealed class MailboxGroupDisplayNames : IStringLocalizer<DisplayNames>
{
    /// <summary>Answers the key as its own text.</summary>
    /// <param name="name">The resource key.</param>
    public LocalizedString this[string name]
    {
        get
        {
            return new LocalizedString(name, name);
        }
    }

    /// <summary>Answers the key as its own text; arguments are ignored.</summary>
    /// <param name="name">The resource key.</param>
    /// <param name="arguments">Ignored.</param>
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            return new LocalizedString(name, name);
        }
    }

    /// <summary>Lists nothing; tests ask for keys by name.</summary>
    /// <param name="includeParentCultures">Ignored.</param>
    /// <returns>An empty list.</returns>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return [];
    }
}
