using Microsoft.Extensions.Localization;

namespace Maran.Modules.Files.Tests.TestSupport;

/// <summary>A localizer that answers every key with the key itself, so a test sees which resource a handler asked for.</summary>
/// <typeparam name="T">The resource table the localizer stands in for.</typeparam>
public sealed class KeyStringLocalizer<T> : IStringLocalizer<T>
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
