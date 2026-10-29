using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// An <see cref="IStringLocalizer{T}"/> double over the module's display names that translates only
/// what a test registered and answers every other key with the key itself — exactly what the real
/// localizer does for a key it has no entry for, so a plan an operator named (whose key is its own
/// text) resolves the way it does in the panel.
/// </summary>
public sealed class StubDisplayNames : IStringLocalizer<DisplayNames>
{
    /// <summary>The translations this double knows, by resource key.</summary>
    private readonly Dictionary<string, string> _texts = [];

    /// <summary>Registers the text a key resolves to.</summary>
    /// <param name="key">The resource key, as a shipped plan stores it.</param>
    /// <param name="text">The translation the request's culture would show.</param>
    /// <returns>This double, so registrations chain.</returns>
    public StubDisplayNames With(string key, string text)
    {
        _texts[key] = text;
        return this;
    }

    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            return _texts.TryGetValue(name, out var text)
                ? new LocalizedString(name, text)
                : new LocalizedString(name, name, resourceNotFound: true);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            return this[name];
        }
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return [];
    }
}
