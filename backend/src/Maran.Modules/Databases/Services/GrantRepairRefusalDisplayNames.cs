using Maran.Modules.Databases.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Databases.Services;

/// <summary>
/// Resolves, in the current request's culture, what the agent decided about a refused grant-table row
/// and what the operator can do about it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The words are the backend's to produce.</b> The refusal vocabulary is the agent's closed enum,
/// and rules/vue.md forbids a machine word on screen while rules/architecture.md puts every domain
/// value's localization here. Built in the same shape as the Backups module's failure names,
/// deliberately, so the panel has one way of naming a stored code rather than two.
/// </para>
/// <para>
/// <b>Two sentences per reason, not one.</b> The name says what the agent decided; the advice says
/// what to do about a row the panel has now promised never to touch. A screen with only the first
/// would show an operator four verdicts on rows that are frequently somebody else's, and no next step
/// — which is the failure this whole surface exists to avoid.
/// </para>
/// <para>
/// <b>The fallback is the machine name, and it is a fallback rather than a promise.</b> An agent newer
/// than this panel can report a reason no bundle here has words for; the operator then reads the
/// identifier, which is loud rather than wrong. That miss is not left to chance — the module's tests
/// walk the agent contract's own enum and fail on the first value with no entry, so the fallback
/// covers a future agent and nothing that ships here.
/// </para>
/// </remarks>
public sealed class GrantRepairRefusalDisplayNames
{
    /// <summary>Prefix of the resx key holding what the agent decided.</summary>
    private const string NameKeyPrefix = "GrantRepairRefusal";

    /// <summary>Prefix of the resx key holding what the operator can do about it.</summary>
    private const string AdviceKeyPrefix = "GrantRepairAdvice";

    /// <summary>This module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public GrantRepairRefusalDisplayNames(IStringLocalizer<DisplayNames> displayNames)
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

    /// <summary>States what the operator can do about one refused row.</summary>
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
    /// string when there is no reason to name at all. The empty case is spelled out rather than left
    /// to the localizer, which would answer with the bare prefix and put the word
    /// <c>GrantRepairRefusal</c> on the screen.
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
