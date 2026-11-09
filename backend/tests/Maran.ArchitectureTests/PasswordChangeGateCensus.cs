using System.Reflection;
using Maran.Modules.Identity.Authorization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.ArchitectureTests;

/// <summary>
/// Reads, without judging, which of the panel's authorization policies carry the first-password gate
/// (<see cref="PasswordChangeCompleteRequirement"/>), and reports the ones that should and do not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule.</b> A login created with an initial password can do nothing but replace it. The gate is
/// a requirement attached to POLICIES rather than to endpoints, so the property to hold is "no policy a
/// signed-in login can satisfy lacks the requirement" — an endpoint added tomorrow under any of those
/// policies is gated without anyone remembering to ask. The fallback and the default policy count too: an
/// endpoint with no metadata or a bare <c>[Authorize]</c> is evaluated against them.
/// </para>
/// <para>
/// <b>The one exemption, and why it is not a gap.</b> A policy that names the <c>ApiKey</c> authentication
/// scheme admits a machine caller, which has no password to replace and no login that could owe the
/// change; the key policies never see a JWT principal at all. They are skipped by that scheme, not by
/// name, so a new key policy is exempt and a new login policy is not.
/// </para>
/// <para>
/// UNOBSERVED HERE: a policy registered by code this census is not handed (a marketplace module calling
/// <c>AddPolicy</c> itself), and an endpoint whose authorization is a bare <c>RequireAssertion</c> with no
/// policy. <c>ControllerAuthorizationDeclarationTests</c> holds every composed controller to the four
/// panel policies and the key policies, which is what makes the names read here the whole set.
/// </para>
/// </remarks>
public static class PasswordChangeGateCensus
{
    /// <summary>The name this census gives the fallback policy in its findings.</summary>
    public const string FallbackName = "(fallback policy)";

    /// <summary>The name this census gives the default policy in its findings.</summary>
    public const string DefaultName = "(default policy)";

    /// <summary>Every policy name the panel defines for a login: the constants of <see cref="AuthorizationPolicies"/>.</summary>
    /// <returns>The four names, ordered ordinally.</returns>
    public static List<string> LoginPolicyNames()
    {
        return ConstantsOf(typeof(AuthorizationPolicies));
    }

    /// <summary>Every policy name that admits an API key: the constants of <see cref="ApiKeyPolicyNames"/>.</summary>
    /// <returns>The key policy names, ordered ordinally.</returns>
    public static List<string> ApiKeyPolicyNameList()
    {
        return ConstantsOf(typeof(ApiKeyPolicyNames));
    }

    /// <summary>Whether a policy admits an API key and therefore a machine caller with no password.</summary>
    /// <param name="policy">The policy.</param>
    /// <returns>True when it names the <c>ApiKey</c> authentication scheme.</returns>
    public static bool AdmitsApiKey(AuthorizationPolicy policy)
    {
        return policy.AuthenticationSchemes.Contains(ApiKeyDefaults.Scheme);
    }

    /// <summary>Runs the rule over the named policies, the fallback and the default.</summary>
    /// <param name="options">The configured authorization options.</param>
    /// <param name="names">The policy names to inspect.</param>
    /// <returns>One ordered line per violation, each beginning with the policy's name.</returns>
    public static List<string> FindUngatedPolicies(AuthorizationOptions options, IEnumerable<string> names)
    {
        var violations = new List<string>();

        foreach (var name in names.OrderBy(candidate => { return candidate; }, StringComparer.Ordinal))
        {
            var policy = options.GetPolicy(name);
            if (policy is null)
            {
                violations.Add($"{name} is named but not registered");
                continue;
            }

            Check(name, policy, violations);
        }

        Check(FallbackName, options.FallbackPolicy, violations);
        Check(DefaultName, options.DefaultPolicy, violations);

        return violations;
    }

    /// <summary>Adds a finding when a policy that should carry the gate does not.</summary>
    /// <param name="name">How the findings name the policy.</param>
    /// <param name="policy">The policy, or null when none is set.</param>
    /// <param name="violations">Where the finding goes.</param>
    private static void Check(string name, AuthorizationPolicy? policy, List<string> violations)
    {
        if (policy is null)
        {
            violations.Add($"{name} is not set, so an endpoint falling back to it is not gated");
            return;
        }

        if (AdmitsApiKey(policy))
        {
            return;
        }

        if (!policy.Requirements.OfType<PasswordChangeCompleteRequirement>().Any())
        {
            violations.Add($"{name} lacks PasswordChangeCompleteRequirement");
        }
    }

    /// <summary>The public string constants of a static class.</summary>
    /// <param name="type">The class.</param>
    /// <returns>Their values, ordered ordinally.</returns>
    private static List<string> ConstantsOf(Type type)
    {
        return type
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => { return field.IsLiteral && field.FieldType == typeof(string); })
            .Select(field => { return (string)field.GetRawConstantValue()!; })
            .OrderBy(value => { return value; }, StringComparer.Ordinal)
            .ToList();
    }
}
