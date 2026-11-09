namespace Maran.Host.Demo.Guard.Enums;

/// <summary>What a visitor of the public demo may do at one mutating endpoint.</summary>
public enum DemoAccess
{
    /// <summary>The call runs. It still draws on the mutation budgets unless its rule says otherwise.</summary>
    Allowed,

    /// <summary>
    /// The call runs, except against a resource the seed marked as protected (an account, reseller or plan
    /// that holds a demo login or gives it its screens); against one of those it is refused.
    /// </summary>
    AllowedUnlessProtected,

    /// <summary>The call is refused whatever it carries, with the area that explains why.</summary>
    Refused,
}
