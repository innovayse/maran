using Maran.Modules.Apps.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Apps.Tests.TestSupport;

/// <summary>A resolver that answers every key with the key, so a test sees which name was asked for.</summary>
public sealed class KeyLocalizer : IStringLocalizer<DisplayNames>
{
    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get { return new LocalizedString(name, name); }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get { return new LocalizedString(name, name); }
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return [];
    }
}
