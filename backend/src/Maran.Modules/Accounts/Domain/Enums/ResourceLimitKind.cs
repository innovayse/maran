namespace Maran.Modules.Accounts.Domain.Enums;

/// <summary>
/// Whether a plan caps a kind of resource. Stated as a value of its own so "no cap" is never read
/// off a sentinel number: a limit of zero is a real cap ("this tier sells none").
/// </summary>
public enum ResourceLimitKind
{
    /// <summary>The plan caps the resource at a stated maximum.</summary>
    Limited,

    /// <summary>The plan states no cap for the resource, so no maximum applies.</summary>
    Unlimited,
}
