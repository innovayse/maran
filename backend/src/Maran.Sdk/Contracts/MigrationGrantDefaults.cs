namespace Maran.Sdk.Contracts;

/// <summary>Names the authentication scheme of a single-use export grant and the header that carries it (issue #127).</summary>
public static class MigrationGrantDefaults
{
    /// <summary>The authentication scheme name. It is never the default scheme; a policy opts into it.</summary>
    public const string Scheme = "MigrationGrant";

    /// <summary>
    /// The prefix of the <c>Authorization</c> header value that carries a grant: <c>Bearer </c>, followed by the
    /// token. Never a query parameter, a path segment or a cookie, so a grant cannot end up in a URL or a log.
    /// </summary>
    public const string BearerPrefix = "Bearer ";
}
