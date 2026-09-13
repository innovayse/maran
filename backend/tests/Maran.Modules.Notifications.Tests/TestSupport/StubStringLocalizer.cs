using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Notifications.Tests.TestSupport;

/// <summary>
/// A localizer that echoes the key and its arguments, so a test can assert WHICH message was chosen
/// without depending on the words in the resx.
/// </summary>
/// <typeparam name="T">The resource marker type the production code asks for.</typeparam>
/// <remarks>
/// Asserting on the English sentence would make every test fail the day a translator improved it,
/// while proving nothing the key does not already prove: the resx triples' completeness is a separate
/// concern with its own guarantee (identical key sets across en, ru and hy).
/// </remarks>
public sealed class StubStringLocalizer<T> : IStringLocalizer<T>
{
    /// <summary>Returns the key itself as the "translation".</summary>
    /// <param name="name">The resource key.</param>
    /// <returns>A found string whose value is the key.</returns>
    public LocalizedString this[string name]
    {
        get
        {
            return new LocalizedString(name, name, resourceNotFound: false);
        }
    }

    /// <summary>Returns the key followed by the arguments, so a test can see what was formatted in.</summary>
    /// <param name="name">The resource key.</param>
    /// <param name="arguments">The values the message would have been formatted with.</param>
    /// <returns>A found string of the form <c>key|arg0|arg1</c>.</returns>
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var joined = string.Join(
                "|",
                arguments.Select(argument =>
                {
                    return Convert.ToString(argument, CultureInfo.InvariantCulture) ?? string.Empty;
                }));

            return new LocalizedString(name, $"{name}|{joined}", resourceNotFound: false);
        }
    }

    /// <summary>Not used by anything under test.</summary>
    /// <param name="includeParentCultures">Ignored.</param>
    /// <returns>An empty sequence.</returns>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        return [];
    }
}
