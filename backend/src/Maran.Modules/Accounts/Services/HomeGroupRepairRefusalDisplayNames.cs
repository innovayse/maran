using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Resolves, in the current request's culture, what the agent decided about a refused hosting
/// account's home and what the operator can do about it.
/// </summary>
/// <remarks>
/// <para>
/// Built in the same shape as the Databases module's <c>GrantRepairRefusalDisplayNames</c>,
/// deliberately, so the panel has one way of naming a stored refusal code rather than two: the
/// vocabulary is the agent's closed enum, and rules/architecture.md puts every domain value's
/// localization on the backend.
/// </para>
/// <para>
/// <b>Two sentences per reason, not one.</b> The name says what the agent decided; the advice says
/// what to do about a home the panel has now promised never to re-group on its own. A screen with
/// only the first would show an operator six verdicts and no next step.
/// </para>
/// <para>
/// <b>The fallback is the machine name, and it is a fallback rather than a promise.</b> An agent newer
/// than this panel can report a reason no bundle here has words for; the operator then reads the
/// identifier, which is loud rather than wrong.
/// </para>
/// </remarks>
public sealed class HomeGroupRepairRefusalDisplayNames
{
    /// <summary>Prefix of the resx key holding what the agent decided.</summary>
    private const string NameKeyPrefix = "HomeGroupRepairRefusal";

    /// <summary>Prefix of the resx key holding what the operator can do about it.</summary>
    private const string AdviceKeyPrefix = "HomeGroupRepairAdvice";

    /// <summary>This module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public HomeGroupRepairRefusalDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one refusal as an operator reads it.</summary>
    /// <param name="reason">The machine-stable refusal name the agent reported.</param>
    /// <returns>The localized name, or <paramref name="reason"/> when this build has no entry for it.</returns>
    public string NameOf(string reason)
    {
        return Read(NameKeyPrefix, reason);
    }

    /// <summary>States what the operator can do about one refused home.</summary>
    /// <param name="reason">The machine-stable refusal name the agent reported.</param>
    /// <returns>The localized advice, or <paramref name="reason"/> when this build has no entry for it.</returns>
    public string AdviceOf(string reason)
    {
        return Read(AdviceKeyPrefix, reason);
    }

    /// <summary>Reads one prefixed entry, falling back to the machine name.</summary>
    /// <param name="prefix">Which of the two sentences to read.</param>
    /// <param name="reason">The machine-stable refusal name the agent reported.</param>
    /// <returns>
    /// The localized text; <paramref name="reason"/> itself when the key is absent, and the empty
    /// string when there is no reason to name at all.
    /// </returns>
    private string Read(string prefix, string reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return string.Empty;
        }

        var localized = _displayNames[prefix + reason];

        return localized.ResourceNotFound ? reason : localized.Value;
    }
}
