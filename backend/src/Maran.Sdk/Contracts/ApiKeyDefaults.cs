namespace Maran.Sdk.Contracts;

/// <summary>Names the API key authentication scheme and the one header that carries a key.</summary>
public static class ApiKeyDefaults
{
    /// <summary>The authentication scheme name. It is never the default scheme; a policy opts into it.</summary>
    public const string Scheme = "ApiKey";

    /// <summary>
    /// The request header a key travels in. Never <c>Authorization</c>, so a key cannot be taken for a
    /// bearer token, and never a query parameter or a cookie.
    /// </summary>
    public const string HeaderName = "X-Api-Key";
}
