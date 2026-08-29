using System.Globalization;
using System.Resources;
using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Localization;

/// <summary>
/// Resolves user-facing text from the <c>.resx</c> resources each module registers for itself
/// (rules/csharp.md "The backend owns all user-facing message text"). A module never shares its
/// resource file with another module; this single, generic implementation tries every registered
/// <see cref="ResourceManager"/> in registration order and returns the first hit for the current
/// UI culture — set per request by <c>RequestLocalizationMiddleware</c> — so a module's error
/// codes and any other resx-backed key (e.g. its display-name key) resolve through the same
/// mechanism without SharedKernel knowing anything about a specific module's resources.
/// </summary>
public sealed class ResxErrorTextProvider : IErrorTextProvider
{
    /// <summary>The resource managers registered by every module that has shipped, in registration order.</summary>
    private readonly IReadOnlyList<ResourceManager> _resourceManagers;

    /// <summary>Creates the provider over every module-registered resource manager.</summary>
    /// <param name="resourceManagers">One <see cref="ResourceManager"/> per module's <c>Resources/Messages.resx</c> family.</param>
    public ResxErrorTextProvider(IEnumerable<ResourceManager> resourceManagers)
    {
        _resourceManagers = resourceManagers.ToList();
    }

    /// <inheritdoc />
    public string Resolve(string code, params object[] arguments)
    {
        foreach (var manager in _resourceManagers)
        {
            var text = manager.GetString(code, CultureInfo.CurrentUICulture);
            if (text is not null)
            {
                return arguments.Length > 0 ? string.Format(CultureInfo.CurrentCulture, text, arguments) : text;
            }
        }

        // No module claims this key. The code itself is the safest fallback: still machine-stable,
        // never a stack trace or tool output, and it makes a missing translation obvious rather
        // than silently swallowed (rules/security.md "Secrets").
        return code;
    }
}
