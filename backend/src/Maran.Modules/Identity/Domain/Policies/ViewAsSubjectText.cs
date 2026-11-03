using System.Diagnostics;
using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// Spells the audit subject of a view-as entry, so the start, its refusal and the end of one session
/// all name the viewed identity the same way (issues #317, #314).
/// </summary>
public static class ViewAsSubjectText
{
    /// <summary>Builds <c>account &lt;name&gt; as &lt;login&gt;</c> or <c>reseller &lt;name&gt; as &lt;login&gt;</c>.</summary>
    /// <param name="kind">What is being viewed.</param>
    /// <param name="subjectName">The account's or the reseller's name.</param>
    /// <param name="loginName">The login being viewed as.</param>
    /// <returns>The subject text recorded with the entry.</returns>
    public static string Of(ViewAsKind kind, string subjectName, string loginName)
    {
        switch (kind)
        {
            case ViewAsKind.Account:
                return $"account {subjectName} as {loginName}";
            case ViewAsKind.Reseller:
                return $"reseller {subjectName} as {loginName}";
            default:
                throw new UnreachableException("ViewAsKind has exactly two cases: Account and Reseller.");
        }
    }
}
