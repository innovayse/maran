namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// The service's JSON as anonymous values, in the service's own spelling (camelCase members, PascalCase
/// enum words). A test serialises one, signs the exact bytes, and sends them: building the documents
/// here, apart from the module's own types, is what lets a test prove the module reads the WIRE shape
/// and not merely its own round trip.
/// </summary>
public static class MarketplaceDocuments
{
    /// <summary>One version of a module.</summary>
    /// <param name="version">The version.</param>
    /// <param name="sha256">The package digest, hexadecimal.</param>
    /// <param name="size">The package size in bytes.</param>
    /// <param name="revoked">Whether the version is withdrawn.</param>
    /// <returns>The JSON value.</returns>
    public static object Version(string version, string sha256, long size, bool revoked = false)
    {
        return new
        {
            version,
            minPanelVersion = "1.0.0",
            sizeBytes = size,
            sha256,
            publishedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            releaseNotes = "Notes",
            revoked,
        };
    }

    /// <summary>One catalogue module.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="latest">The newest version, or null.</param>
    /// <param name="revoked">Whether the module is withdrawn.</param>
    /// <returns>The JSON value.</returns>
    public static object Module(string id, object? latest, bool revoked = false)
    {
        return new
        {
            id,
            displayName = "Mail",
            summary = "Mailboxes",
            publisher = "Innovayse",
            tier = "AddOn",
            entitlement = new { state = "Entitled" },
            revoked,
            latest,
        };
    }

    /// <summary>A catalogue page.</summary>
    /// <param name="signedAt">When it was signed.</param>
    /// <param name="validUntil">Until when it is good.</param>
    /// <param name="modules">The modules on the page.</param>
    /// <returns>The JSON value.</returns>
    public static object Page(DateTimeOffset signedAt, DateTimeOffset validUntil, params object[] modules)
    {
        return new { items = modules, nextCursor = (string?)null, signedAt, validUntil };
    }

    /// <summary>A module's details answer.</summary>
    /// <param name="signedAt">When it was signed.</param>
    /// <param name="validUntil">Until when it is good.</param>
    /// <param name="module">The module.</param>
    /// <param name="versions">Every offered version.</param>
    /// <returns>The JSON value.</returns>
    public static object Details(DateTimeOffset signedAt, DateTimeOffset validUntil, object module, params object[] versions)
    {
        return new { module, versions, signedAt, validUntil };
    }

    /// <summary>A download grant.</summary>
    /// <param name="url">The download address.</param>
    /// <param name="sha256">The declared digest.</param>
    /// <param name="size">The declared size.</param>
    /// <param name="expiresAt">When the grant stops being accepted.</param>
    /// <returns>The JSON value.</returns>
    public static object Grant(string url, string sha256, long size, DateTimeOffset expiresAt)
    {
        return new { grantId = "grant-secret-id", url, expiresAt, sha256, sizeBytes = size, encrypted = true };
    }

    /// <summary>An updates answer.</summary>
    /// <param name="signedAt">When it was signed.</param>
    /// <param name="validUntil">Until when it is good.</param>
    /// <returns>The JSON value.</returns>
    public static object Updates(DateTimeOffset signedAt, DateTimeOffset validUntil)
    {
        return new { items = Array.Empty<object>(), signedAt, validUntil };
    }

    /// <summary>The service's key list.</summary>
    /// <param name="signedAt">When it was signed.</param>
    /// <param name="validUntil">Until when it is good.</param>
    /// <param name="publicKeyPems">The announced keys.</param>
    /// <returns>The JSON value.</returns>
    public static object Keys(DateTimeOffset signedAt, DateTimeOffset validUntil, params string[] publicKeyPems)
    {
        return new
        {
            keys = publicKeyPems.Select((pem, index) => { return new { id = $"key-{index}", publicKeyPem = pem }; }).ToArray(),
            signedAt,
            validUntil,
        };
    }
}
