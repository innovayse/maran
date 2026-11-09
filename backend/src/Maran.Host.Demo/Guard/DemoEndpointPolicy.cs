using System.Reflection;

namespace Maran.Host.Demo.Guard;

/// <summary>A lookup over a table of <see cref="DemoEndpointRule"/> keyed by action id.</summary>
/// <remarks>
/// A class over a table, not the table itself, so the census test can hold a planted table against a
/// planted controller and the real one against the real controllers with the same code.
/// </remarks>
public sealed class DemoEndpointPolicy
{
    /// <summary>The policy over the real table, <see cref="DemoEndpointTable.Rules"/>.</summary>
    public static readonly DemoEndpointPolicy Default = new(DemoEndpointTable.Rules);

    /// <summary>The classified actions.</summary>
    private readonly IReadOnlyDictionary<string, DemoEndpointRule> _rules;

    /// <summary>Every action id the table names.</summary>
    public IReadOnlyCollection<string> ActionIds
    {
        get { return _rules.Keys.ToList(); }
    }

    /// <summary>Creates a policy over a table.</summary>
    /// <param name="rules">The rules by action id.</param>
    public DemoEndpointPolicy(IReadOnlyDictionary<string, DemoEndpointRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    /// <summary>The id an action is classified under: its declaring type's name, a dot, its method name.</summary>
    /// <param name="action">The action method.</param>
    /// <returns>For example <c>AuthController.ChangePasswordAsync</c>.</returns>
    public static string ActionIdOf(MethodInfo action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return $"{action.DeclaringType!.Name}.{action.Name}";
    }

    /// <summary>Finds the rule for an action.</summary>
    /// <param name="actionId">The id from <see cref="ActionIdOf"/>.</param>
    /// <returns>The rule, or <c>null</c> when the table does not name the action.</returns>
    public DemoEndpointRule? RuleFor(string actionId)
    {
        return _rules.TryGetValue(actionId, out var rule) ? rule : null;
    }
}
