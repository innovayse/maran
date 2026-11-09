namespace Maran.Modules.Databases.Domain.Policies;

/// <summary>The ceilings on the database users one account may hold.</summary>
/// <remarks>
/// Constants and not plan fields: a plan field touches the plan entity, its migration, its screens and
/// the reseller envelope, and nobody has asked to sell this number. The day someone does, the value moves
/// to the plan and this class is where its default lives.
/// </remarks>
public static class DatabaseUserLimits
{
    /// <summary>
    /// How many additional database users an account may create, not counting the dedicated user each
    /// of its databases already has.
    /// </summary>
    /// <remarks>
    /// A dedicated user is bounded by the plan's database allowance, which it is created under, so it
    /// is not counted here: counting it would let a plan with fifty databases create none. Every user
    /// is a login on the host's one database server, so an unbounded count of the rest is a way for one
    /// account to fill the server's user table. Fifty is far above any use of a handful of databases
    /// and far below a server's own ceiling.
    /// </remarks>
    public const int MaximumExtraUsersPerAccount = 50;
}
