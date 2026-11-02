using Maran.Modules.Databases.Domain.Enums;
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
/// <b>There is no fallback, because there is no unnamed reason.</b> The reasons are
/// <see cref="GrantRefusalReason"/>, a closed enum the agent client fills only from values it knows; a
/// reason from an agent newer than this panel refuses the whole reply before it reaches here. The
/// guarantee that every member has words is the module's tests, which walk the enum and fail on the
/// first member with no entry in a language.
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
    /// <param name="reason">The refusal the agent reported.</param>
    /// <returns>The localized name.</returns>
    public string NameOf(GrantRefusalReason reason)
    {
        return _displayNames[NameKeyPrefix + reason].Value;
    }

    /// <summary>States what the operator can do about one refused row.</summary>
    /// <param name="reason">The refusal the agent reported.</param>
    /// <returns>The localized advice.</returns>
    public string AdviceOf(GrantRefusalReason reason)
    {
        return _displayNames[AdviceKeyPrefix + reason].Value;
    }
}
