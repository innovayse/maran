namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>What a panel <see cref="Entities.User"/> is allowed to reach (spec §8).</summary>
public enum UserRole
{
    /// <summary>Full control of the server and every hosting account on it.</summary>
    Admin,

    /// <summary>A hosting customer: their own accounts and nothing else.</summary>
    Customer,

    /// <summary>
    /// A reseller: the one login of a business that sells hosting on the server. It manages the plans
    /// and accounts it owns and can view as them, and it holds no membership of any account.
    /// </summary>
    Reseller,
}
