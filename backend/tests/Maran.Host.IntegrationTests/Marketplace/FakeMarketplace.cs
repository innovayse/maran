using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Maran.SharedKernel.Interfaces;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// A stand-in for the module marketplace: it counts every request that reaches it and answers the
/// catalogue, module details, update check, download grant and package download with documents signed by
/// the key it was given. Nothing here is a real service, and nothing about it is configurable from the
/// panel's side, so a test that sees a request here knows the panel really sent it.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: TLS, DNS and a real network stack. The handler under the panel's named client answers
/// in memory, so what is observed is every decision the panel makes before it would open a connection and
/// every byte it would put on one.
/// </remarks>
public sealed class FakeMarketplace
{
    /// <summary>The host name the panel is configured to talk to.</summary>
    public const string Host = "market.test";

    /// <summary>The panel's configured marketplace address.</summary>
    public const string BaseUrl = "https://market.test";

    /// <summary>The download-grant id the marketplace hands out: a planted secret the leak tests search for.</summary>
    public const string GrantId = "grant-secret-5e21b7d4";

    /// <summary>The path segment that makes the download address unique: a planted secret of the leak tests.</summary>
    public const string DownloadSegment = "pkg-9c4d17e2";

    /// <summary>The response header the panel reads the signature from.</summary>
    private const string SignatureHeader = "X-Maran-Signature";

    /// <summary>The JSON dialect the marketplace speaks.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Every request received, in arrival order.</summary>
    private readonly ConcurrentQueue<RecordedMarketplaceRequest> _requests = new();

    /// <summary>The key the answers are signed with.</summary>
    private readonly MarketplaceKeyPair _signingKey;

    /// <summary>The time source the signed documents' dates come from.</summary>
    private readonly IClock _clock;

    /// <summary>The id of the one module the marketplace offers.</summary>
    private readonly string _moduleId;

    /// <summary>The one version of that module the marketplace offers.</summary>
    private readonly string _version;

    /// <summary>The bytes of that version's package.</summary>
    private readonly byte[] _package;

    /// <summary>The address the download grant names; a test points it elsewhere to plant a hostile grant.</summary>
    public string GrantUrl { get; set; } = BaseUrl + "/download/" + DownloadSegment + "/package.ipm";

    /// <summary>The lowest panel version the offered version runs on; the test sets the running panel's own.</summary>
    public string MinPanelVersion { get; set; } = "0.0.1";

    /// <summary>The lower-case hex SHA-256 of the package the marketplace serves.</summary>
    public string PackageSha256 { get; }

    /// <summary>The size of the package the marketplace serves, in bytes.</summary>
    public long PackageSize { get; }

    /// <summary>Every request received, in arrival order.</summary>
    public IReadOnlyList<RecordedMarketplaceRequest> Requests
    {
        get
        {
            return _requests.ToArray();
        }
    }

    /// <summary>How many requests have been received.</summary>
    public int RequestCount
    {
        get
        {
            return _requests.Count;
        }
    }

    /// <summary>Creates the marketplace.</summary>
    /// <param name="signingKey">The key every signed answer is signed with.</param>
    /// <param name="clock">The time source the signed documents' dates come from.</param>
    /// <param name="moduleId">The id of the one module on offer.</param>
    /// <param name="version">The one version of it on offer.</param>
    /// <param name="package">The package bytes of that version.</param>
    public FakeMarketplace(
        MarketplaceKeyPair signingKey,
        IClock clock,
        string moduleId,
        string version,
        byte[] package)
    {
        _signingKey = signingKey;
        _clock = clock;
        _moduleId = moduleId;
        _version = version;
        _package = package;
        PackageSha256 = Convert.ToHexStringLower(SHA256.HashData(package));
        PackageSize = package.Length;
    }

    /// <summary>Counts a request and answers it.</summary>
    /// <param name="request">The request the panel sent.</param>
    /// <returns>The answer; the caller owns and disposes it.</returns>
    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        _requests.Enqueue(new RecordedMarketplaceRequest(
            request.Method.Method, uri.Host, uri.AbsolutePath, request.Headers.Authorization?.ToString()));

        var path = uri.AbsolutePath;
        var modulePath = "/api/v1/modules/" + _moduleId;

        if (request.Method == HttpMethod.Get && path == "/api/v1/catalogue")
        {
            return Signed(CataloguePage());
        }

        if (request.Method == HttpMethod.Get && path == modulePath)
        {
            return Signed(ModuleDetails());
        }

        if (request.Method == HttpMethod.Post && path == modulePath + "/versions/" + _version + "/download-grant")
        {
            return Signed(DownloadGrant());
        }

        if (request.Method == HttpMethod.Post && path == "/api/v1/updates")
        {
            return Signed(UpdateReport());
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/download/", StringComparison.Ordinal))
        {
            return Package();
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>The version document the catalogue and the module details carry.</summary>
    /// <returns>The document.</returns>
    private object VersionDocument()
    {
        return new
        {
            version = _version,
            minPanelVersion = MinPanelVersion,
            sizeBytes = PackageSize,
            sha256 = PackageSha256,
            publishedAt = _clock.UtcNow.AddDays(-1),
            releaseNotes = "First release.",
            revoked = false,
        };
    }

    /// <summary>The module document the catalogue and the module details carry.</summary>
    /// <returns>The document.</returns>
    private object ModuleDocument()
    {
        return new
        {
            id = _moduleId,
            displayName = "Hello module",
            summary = "A module the tests install.",
            publisher = "Innovayse",
            tier = "AddOn",
            entitlement = new { state = "Entitled" },
            revoked = false,
            latest = VersionDocument(),
        };
    }

    /// <summary>The catalogue's one page.</summary>
    /// <returns>The document, fresh at the time of the request.</returns>
    private object CataloguePage()
    {
        return new
        {
            items = new[] { ModuleDocument() },
            nextCursor = (string?)null,
            signedAt = _clock.UtcNow.AddMinutes(-1),
            validUntil = _clock.UtcNow.AddHours(12),
        };
    }

    /// <summary>The details of the one module on offer.</summary>
    /// <returns>The document, fresh at the time of the request.</returns>
    private object ModuleDetails()
    {
        return new
        {
            module = ModuleDocument(),
            versions = new[] { VersionDocument() },
            signedAt = _clock.UtcNow.AddMinutes(-1),
            validUntil = _clock.UtcNow.AddHours(12),
        };
    }

    /// <summary>The download grant for the offered version.</summary>
    /// <returns>The document, naming <see cref="GrantUrl"/> and valid for ten minutes.</returns>
    private object DownloadGrant()
    {
        return new
        {
            grantId = GrantId,
            url = GrantUrl,
            expiresAt = _clock.UtcNow.AddMinutes(10),
            sha256 = PackageSha256,
            sizeBytes = PackageSize,
            encrypted = false,
        };
    }

    /// <summary>The answer to an update check: nothing newer and nothing withdrawn.</summary>
    /// <returns>The document, fresh at the time of the request.</returns>
    private object UpdateReport()
    {
        return new
        {
            items = Array.Empty<object>(),
            signedAt = _clock.UtcNow.AddMinutes(-1),
            validUntil = _clock.UtcNow.AddHours(12),
        };
    }

    /// <summary>Serializes a document and signs the exact bytes the panel will receive.</summary>
    /// <param name="document">The document.</param>
    /// <returns>A 200 carrying the document and its detached signature.</returns>
    private HttpResponseMessage Signed(object document)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        response.Headers.TryAddWithoutValidation(SignatureHeader, _signingKey.Sign(body));

        return response;
    }

    /// <summary>The package download. Unsigned: the panel judges it by the digest the signed grant carries.</summary>
    /// <returns>A 200 carrying the package bytes.</returns>
    private HttpResponseMessage Package()
    {
        var content = new ByteArrayContent(_package);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
