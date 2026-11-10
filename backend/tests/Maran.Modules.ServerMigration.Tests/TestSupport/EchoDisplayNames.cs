using Maran.Modules.ServerMigration.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.ServerMigration.Tests.TestSupport;

/// <summary>A display-name lookup that answers each key with itself, so a test sees WHICH name a mapper asked for.</summary>
public sealed class EchoDisplayNames : IStringLocalizer<DisplayNames>
{
    /// <summary>Answers the key.</summary>
    /// <param name="name">The key.</param>
    public LocalizedString this[string name]
    {
        get { return new LocalizedString(name, name); }
    }

    /// <summary>Answers the key followed by its arguments, so a test sees what a sentence was built from.</summary>
    /// <param name="name">The key.</param>
    /// <param name="arguments">The sentence's arguments.</param>
    public LocalizedString this[string name, params object[] arguments]
    {
        get { return new LocalizedString(name, name + "|" + string.Join("|", arguments)); }
    }

    /// <summary>Lists nothing; a test asks for a key by name.</summary>
    /// <param name="includeParentCultures">Ignored.</param>
    /// <returns>No strings.</returns>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return [];
    }
}
