namespace Maran.Modules.Identity.Queries.ListApiKeys;

/// <summary>
/// The public names the API key list sorts by — the <c>sort</c> parameter's vocabulary, written once
/// and read by <see cref="ListApiKeysSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class ApiKeySortFields
{
    /// <summary>The label the administrator gave the key.</summary>
    public const string Name = "name";

    /// <summary>When the key was created.</summary>
    public const string CreatedAt = "createdAt";

    /// <summary>When the key was last used; a key never used sorts as the earliest.</summary>
    public const string LastUsedAt = "lastUsedAt";
}
