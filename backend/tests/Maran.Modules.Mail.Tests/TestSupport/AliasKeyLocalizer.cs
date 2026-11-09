using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>A localizer that answers every key with the key itself, so a test sees which name a handler asked for.</summary>
/// <typeparam name="T">The resource table the localizer stands in for.</typeparam>
public sealed class AliasKeyLocalizer<T> : IStringLocalizer<T>
{
    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            return new LocalizedString(name, name);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            return new LocalizedString(name, name);
        }
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return [];
    }
}
