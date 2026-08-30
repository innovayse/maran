namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>What a panel <see cref="User"/> is allowed to reach (spec §8).</summary>
public enum UserRole
{
    /// <summary>Full control of the server and every hosting account on it.</summary>
    Admin,

    /// <summary>A hosting customer: their own accounts and nothing else.</summary>
    Customer,
}
