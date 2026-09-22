using System.Globalization;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// Sets the current UI culture for the length of a <c>using</c> block and puts back what was there.
/// Same shape as the Databases module's own <c>CultureScope</c> — see its remarks.
/// </summary>
public sealed class CultureScope : IDisposable
{
    /// <summary>The culture that was current when this scope was entered.</summary>
    private readonly CultureInfo _previous;

    /// <summary>Switches the current UI culture to <paramref name="culture"/>.</summary>
    /// <param name="culture">A culture name such as <c>ru</c>.</param>
    public CultureScope(string culture)
    {
        _previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
    }

    /// <summary>Puts back the culture that was current before this scope.</summary>
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _previous;
    }
}
