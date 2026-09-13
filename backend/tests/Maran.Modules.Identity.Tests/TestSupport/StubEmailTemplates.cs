using Maran.Modules.Identity.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="IStringLocalizer{T}"/> double that returns each key's own name as its text, so a
/// test can assert on WHICH template was used without depending on the English sentence in the resx.
/// </summary>
public sealed class StubEmailTemplates : IStringLocalizer<EmailTemplates>
{
    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            return new LocalizedString(name, name + ":{0}");
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            return new LocalizedString(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, name + ":{0}", arguments));
        }
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return [];
    }
}
