using System.Diagnostics.CodeAnalysis;

namespace Maran.SharedKernel.Interfaces;

/// <summary>
/// Resolves localized, customer-facing text for a domain error code. <see cref="Results.Error.Code"/>
/// stays machine-stable and untranslated; this is the seam that turns it into the text placed in an
/// RFC 7807 <c>ProblemDetails</c> title/detail (rules/csharp.md "The backend owns all user-facing
/// message text"). The shipped implementation,
/// <see cref="Localization.ResxErrorTextProvider"/>, resolves against the
/// <c>Resources/Messages*.resx</c> tables each module registers for itself and against the current
/// request culture set by <c>RequestLocalizationMiddleware</c>. SharedKernel therefore needs no
/// reference to any module: a module hands over its own <c>ResourceManager</c>, and the provider
/// reads it.
/// </summary>
public interface IErrorTextProvider
{
    /// <summary>Returns the localized message for <paramref name="code"/> in the current request culture.</summary>
    /// <param name="code">The machine-stable error code (e.g. <c>"SitesDomainTaken"</c>).</param>
    /// <param name="arguments">Optional format arguments substituted into the resolved message.</param>
    string Resolve(string code, params object[] arguments);

    /// <summary>
    /// Looks up the localized message for <paramref name="code"/> in the current request culture, and
    /// says whether any registered table claims it.
    /// </summary>
    /// <param name="code">The machine-stable error code.</param>
    /// <param name="text">The resolved message when found; <c>null</c> otherwise.</param>
    /// <returns><c>true</c> when a table claims <paramref name="code"/>.</returns>
    /// <remarks>
    /// The same lookup <see cref="Resolve"/> performs, exposed for a caller whose answer to a miss is
    /// a sentence of its own rather than the code: a stored task failure is read long after the
    /// operation that raised it, possibly by a build that no longer ships the code, and the reader
    /// must still be shown a sentence. A miss is reported the way <see cref="Resolve"/> reports one.
    /// </remarks>
    bool TryResolve(string code, [NotNullWhen(true)] out string? text);
}
