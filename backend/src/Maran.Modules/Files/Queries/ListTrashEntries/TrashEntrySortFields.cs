namespace Maran.Modules.Files.Queries.ListTrashEntries;

/// <summary>The names a client may sort the trash by, as constants so no caller spells them twice.</summary>
public static class TrashEntrySortFields
{
    /// <summary>By the entry's own name.</summary>
    public const string Name = "name";

    /// <summary>By when the entry was moved to the trash.</summary>
    public const string DeletedAt = "deletedAt";

    /// <summary>By when the retention job will remove the entry for good.</summary>
    public const string ExpiresAt = "expiresAt";

    /// <summary>By the entry's size.</summary>
    public const string Size = "size";
}
