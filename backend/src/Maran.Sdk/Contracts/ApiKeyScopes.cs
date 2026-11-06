namespace Maran.Sdk.Contracts;

/// <summary>
/// The closed vocabulary of scopes an API key can hold. A scope bounds what a key may reach; nothing
/// outside this list is granted, and a stored scope this build does not know grants nothing.
/// </summary>
public static class ApiKeyScopes
{
    /// <summary>Reads the key's own description. Implicit: every valid key holds it.</summary>
    public const string SelfRead = "self.read";

    /// <summary>Reads server-wide administrator data that belongs to no tenant.</summary>
    public const string ServerRead = "server.read";

    /// <summary>Every scope a key can hold, in the order the panel lists them.</summary>
    public static readonly IReadOnlyList<string> All = [SelfRead, ServerRead];
}
