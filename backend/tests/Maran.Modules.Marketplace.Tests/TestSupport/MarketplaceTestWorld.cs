using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Maran.Modules.Marketplace.Options;
using Maran.Modules.Marketplace.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>
/// The whole outbound half wired over a scripted handler: real transport, checker, cache, downloader and
/// client; fake wire, clock, switch and lease. A test scripts the handler, calls the client, and reads
/// the counters. Everything on disk lives under a private temp directory removed on disposal, and the
/// signing key is generated here and never written anywhere but that directory's public key file.
/// </summary>
public sealed class MarketplaceTestWorld : IDisposable
{
    /// <summary>The configured host.</summary>
    public const string HostUrl = "https://marketplace.test";

    /// <summary>The JSON options the service's documents are written with.</summary>
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    /// <summary>The private temp directory everything on disk lives under.</summary>
    public string Root { get; }

    /// <summary>The pinned key file's path.</summary>
    public string KeyFile { get; }

    /// <summary>The directory packages are downloaded into (created by the downloader, not by the world).</summary>
    public string PackageDirectory { get; }

    /// <summary>The key the service signs with and the pinned file holds.</summary>
    public MarketplaceSigner Signer { get; }

    /// <summary>The options the module reads; a test may change them.</summary>
    public MarketplaceOptions Options { get; }

    /// <summary>The test clock.</summary>
    public AdvancingClock Clock { get; } = new();

    /// <summary>The marketplace switch.</summary>
    public StubMarketplaceSwitch Switch { get; }

    /// <summary>The lease authorizer.</summary>
    public StubLeaseAuthorizer Lease { get; }

    /// <summary>The handler that answers and counts.</summary>
    public ScriptedHandler Handler { get; }

    /// <summary>The factory the transport asks for a client.</summary>
    public ScriptedHttpClientFactory Factory { get; }

    /// <summary>The signature checker.</summary>
    public CatalogueSignatureChecker Checker { get; }

    /// <summary>The catalogue cache.</summary>
    public CatalogueCache Cache { get; }

    /// <summary>The real transport.</summary>
    public MarketplaceHttpTransport Transport { get; }

    /// <summary>The package downloader.</summary>
    public PackageDownloader Downloader { get; }

    /// <summary>The client under test.</summary>
    public MarketplaceClient Client { get; }

    /// <summary>Creates the world with the pinned key file written and the marketplace switched on.</summary>
    /// <param name="writeKeyFile">Whether the pinned key file exists.</param>
    public MarketplaceTestWorld(bool writeKeyFile = true)
    {
        Root = Directory.CreateTempSubdirectory("maran-marketplace-test-").FullName;
        Signer = new MarketplaceSigner();
        KeyFile = Path.Combine(Root, "marketplace-metadata.pub");
        PackageDirectory = Path.Combine(Root, "packages");

        if (writeKeyFile)
        {
            File.WriteAllText(KeyFile, Signer.PublicKeyPem);
        }

        Options = new MarketplaceOptions
        {
            BaseUrl = HostUrl,
            MetadataKeyPath = KeyFile,
            PackageDirectory = PackageDirectory,
        };
        Switch = new StubMarketplaceSwitch { Enabled = true };
        Lease = new StubLeaseAuthorizer();
        Handler = new ScriptedHandler();
        Factory = new ScriptedHttpClientFactory(Handler);

        var reader = new MarketplaceOptionsReader(new TestOptionsMonitor<MarketplaceOptions>(Options));
        Checker = new CatalogueSignatureChecker(reader, Clock);
        Cache = new CatalogueCache(reader, Clock);
        Transport = new MarketplaceHttpTransport(
            Factory, reader, new FixedPanelVersion(), Lease, NullLogger<MarketplaceHttpTransport>.Instance);
        Downloader = new PackageDownloader(reader, Clock, NullLogger<PackageDownloader>.Instance);
        Client = new MarketplaceClient(
            Switch,
            Transport,
            new DisabledMarketplaceTransport(),
            new SignedResponseReader(Checker),
            Checker,
            Cache,
            new LeaseFingerprintReader(Lease),
            Downloader);
    }

    /// <summary>The lower-case hexadecimal SHA-256 of some bytes.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The digest as hex.</returns>
    public static string Sha256Of(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Serialises a document to the exact bytes that get signed and sent.</summary>
    /// <param name="document">The JSON value.</param>
    /// <returns>The UTF-8 bytes.</returns>
    public static byte[] BytesOf(object document)
    {
        return JsonSerializer.SerializeToUtf8Bytes(document, WireJson);
    }

    /// <summary>A 200 answer carrying a document, signed by the pinned key.</summary>
    /// <param name="document">The JSON value.</param>
    /// <param name="etag">An ETag to send, or null.</param>
    /// <returns>The answer.</returns>
    public HttpResponseMessage SignedResponse(object document, string? etag = null)
    {
        return SignedBy(Signer, document, etag);
    }

    /// <summary>A 200 answer carrying a document, signed by the given key.</summary>
    /// <param name="signer">The key to sign with.</param>
    /// <param name="document">The JSON value.</param>
    /// <param name="etag">An ETag to send, or null.</param>
    /// <returns>The answer.</returns>
    public static HttpResponseMessage SignedBy(MarketplaceSigner signer, object document, string? etag = null)
    {
        var bytes = BytesOf(document);
        var response = Raw(HttpStatusCode.OK, bytes);
        response.Headers.TryAddWithoutValidation("X-Maran-Signature", signer.Sign(bytes));

        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return response;
    }

    /// <summary>A 200 answer carrying a document and no signature at all.</summary>
    /// <param name="document">The JSON value.</param>
    /// <returns>The answer.</returns>
    public static HttpResponseMessage UnsignedResponse(object document)
    {
        return Raw(HttpStatusCode.OK, BytesOf(document));
    }

    /// <summary>An answer with a status and a body.</summary>
    /// <param name="status">The status.</param>
    /// <param name="body">The body bytes.</param>
    /// <returns>The answer.</returns>
    public static HttpResponseMessage Raw(HttpStatusCode status, byte[] body)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    /// <summary>The service's failure answer: a status and a problem document with a code.</summary>
    /// <param name="status">The status.</param>
    /// <param name="code">The service's code.</param>
    /// <returns>The answer.</returns>
    public static HttpResponseMessage Problem(HttpStatusCode status, string code)
    {
        return Raw(status, BytesOf(new { code, title = "ignored" }));
    }

    /// <summary>Removes the temp directory.</summary>
    public void Dispose()
    {
        Signer.Dispose();
        Directory.Delete(Root, recursive: true);
    }
}
