using Microsoft.Extensions.Localization;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A localizer that answers every key with the key itself and reports it as found.</summary>
/// <typeparam name="T">The resource marker type.</typeparam>
public sealed class KeyLocalizer<T> : IStringLocalizer<T>
{
    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            return new LocalizedString(name, name, resourceNotFound: false);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            return new LocalizedString(name, name, resourceNotFound: false);
        }
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return [];
    }
}
