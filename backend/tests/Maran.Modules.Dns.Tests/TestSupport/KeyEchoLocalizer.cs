using Maran.Modules.Dns.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>An <see cref="IStringLocalizer{T}"/> that answers every key with the key itself.</summary>
public sealed class KeyEchoLocalizer : IStringLocalizer<DisplayNames>
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
