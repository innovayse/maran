namespace Maran.Modules.Identity.Queries.ListSubUsers;

/// <summary>The names a client may sort the sub-users list by.</summary>
public static class SubUserSortFields
{
    /// <summary>The login name.</summary>
    public const string Username = "username";

    /// <summary>The address.</summary>
    public const string Email = "email";

    /// <summary>When the person was added.</summary>
    public const string CreatedAt = "createdAt";
}
