using Maran.Modules.Identity.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Resolves the administrator-facing name of an API key scope in the current request's culture.
/// </summary>
/// <remarks>
/// The scope's resx key is <c>ApiKeyScope</c> followed by each dot-separated word of the scope
/// capitalised (<c>self.read</c> is <c>ApiKeyScopeSelfRead</c>), spelled in this one method. The
/// vocabulary is the Sdk's closed <c>ApiKeyScopes</c> set, and the module's tests walk it and fail on
/// the first scope with no entry; a scope this build carries no entry for is named by itself, which
/// is exactly what the screen would otherwise have to print.
/// </remarks>
public sealed class ApiKeyScopeDisplayNames
{
    /// <summary>The prefix a scope's resx key carries, so scopes cannot collide with other names.</summary>
    private const string KeyPrefix = "ApiKeyScope";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public ApiKeyScopeDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one scope as an administrator reads it.</summary>
    /// <param name="scope">The machine-stable scope, from the Sdk's <c>ApiKeyScopes</c>.</param>
    /// <returns>The localized name, or <paramref name="scope"/> itself when this build carries no entry for it.</returns>
    public string Of(string scope)
    {
        var words = scope.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var key = KeyPrefix + string.Concat(words.Select(word =>
        {
            return char.ToUpperInvariant(word[0]) + word[1..];
        }));

        var localized = _displayNames[key];

        return localized.ResourceNotFound ? scope : localized.Value;
    }
}
