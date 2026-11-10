using Maran.ArchitectureTests.Fixtures;

namespace Maran.ArchitectureTests;

/// <summary>
/// The source half of the marketplace's outbound gate (plan 4.G, G4 i and the source side of iii): the
/// module has exactly one place that touches an HTTP client, one place that registers the named client, and
/// no code that runs on its own schedule. The behavioural half (a switched-off marketplace sends nothing, a
/// switched-on one does, composing the registrations sends nothing) lives in
/// <c>Maran.Modules.Marketplace.Tests/Services/MarketplaceOutboundGateTests</c>, where the module can be
/// executed.
/// </summary>
/// <remarks>
/// Each scan carries a planted violation it must find and a clean source it must not, and the scan of the
/// real tree is shown able to see: the one allowed file is found to contain what the rule forbids elsewhere.
/// A scan that reported "nothing outside the transport" because it read no files would pass loudest when it
/// had stopped looking.
/// </remarks>
public sealed class MarketplaceOutboundIsGatedTests
{
    /// <summary>The only file allowed to touch an HTTP client, relative to the module folder.</summary>
    private const string TransportFile = "Services/MarketplaceHttpTransport.cs";

    /// <summary>The only file allowed to register the named client, relative to the module folder.</summary>
    private const string RegistrationFile = "Extensions/MarketplaceClientExtensions.cs";

    /// <summary>The least number of source files the module must show, so an empty scan cannot pass.</summary>
    private const int LeastSourceFiles = 20;

    /// <summary>Absolute path of the Marketplace module's folder.</summary>
    /// <returns>The folder.</returns>
    private static string ModuleFolder()
    {
        return Path.Combine(ResourceTriples.BackendRoot(), "src", "Maran.Modules", "Marketplace");
    }

    /// <summary>The relative form of a path inside the module, with forward slashes.</summary>
    /// <param name="path">An absolute path inside the module folder.</param>
    /// <returns>The relative path.</returns>
    private static string Relative(string path)
    {
        return Path.GetRelativePath(ModuleFolder(), path).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>No file of the module but the transport names an HTTP client, a client factory or a raw network type.</summary>
    [Fact]
    public void Only_the_transport_touches_an_http_client()
    {
        var offenders = MarketplaceOutboundScanner.SourceFiles(ModuleFolder())
            .Where(file => { return Relative(file) != TransportFile; })
            .Select(file => { return (Name: Relative(file), Uses: MarketplaceOutboundScanner.NetworkUses(File.ReadAllText(file))); })
            .Where(found => { return found.Uses.Count > 0; })
            .Select(found => { return $"{found.Name}: {string.Join(", ", found.Uses)}"; })
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Only Services/MarketplaceHttpTransport.cs may touch an HTTP client in the Marketplace module:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>The scan of the real tree can see: it reads the module, and the transport is found to use what the rule forbids elsewhere.</summary>
    [Fact]
    public void The_scan_reads_the_module_and_sees_the_transports_client()
    {
        var files = MarketplaceOutboundScanner.SourceFiles(ModuleFolder());
        var transport = File.ReadAllText(Path.Combine(ModuleFolder(), TransportFile.Replace('/', Path.DirectorySeparatorChar)));

        Assert.True(files.Count >= LeastSourceFiles, $"The scan read {files.Count} files; the module has more than that.");
        Assert.Contains("IHttpClientFactory", MarketplaceOutboundScanner.NetworkUses(transport));
        Assert.Contains("HttpClient", MarketplaceOutboundScanner.NetworkUses(transport));
        Assert.Contains("SocketsHttpHandler", MarketplaceOutboundScanner.NetworkUses(transport));
    }

    /// <summary>A planted service that builds its own client or asks the factory is found; a clean source with the words in comments, an AddHttpClient call and a URL is not.</summary>
    [Fact]
    public void A_planted_use_of_an_http_client_is_found_and_a_clean_source_is_not()
    {
        Assert.Equal(["HttpClient"], MarketplaceOutboundScanner.NetworkUses(PlantedMarketplaceSources.OwnHttpClient));
        Assert.Equal(["IHttpClientFactory"], MarketplaceOutboundScanner.NetworkUses(PlantedMarketplaceSources.OwnClientFactory));
        Assert.Empty(MarketplaceOutboundScanner.NetworkUses(PlantedMarketplaceSources.Clean));
    }

    /// <summary>The named client is registered by the registration extension and nowhere else in the module.</summary>
    [Fact]
    public void The_named_client_is_registered_only_by_the_registration_extension()
    {
        var registering = MarketplaceOutboundScanner.SourceFiles(ModuleFolder())
            .Where(file => { return MarketplaceOutboundScanner.WithoutComments(File.ReadAllText(file)).Contains(".AddHttpClient(", StringComparison.Ordinal); })
            .Select(Relative)
            .ToList();

        Assert.Equal([RegistrationFile], registering);
    }

    /// <summary>The module runs nothing on its own: no hosted service, background service, timer or job scheduler.</summary>
    [Fact]
    public void The_module_has_no_code_that_runs_on_its_own()
    {
        var offenders = MarketplaceOutboundScanner.SourceFiles(ModuleFolder())
            .Select(file => { return (Name: Relative(file), Uses: MarketplaceOutboundScanner.BackgroundUses(File.ReadAllText(file))); })
            .Where(found => { return found.Uses.Count > 0; })
            .Select(found => { return $"{found.Name}: {string.Join(", ", found.Uses)}"; })
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "The Marketplace module must start no work of its own (rules/security.md item 10):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>A planted background caller is found, and a clean source is not.</summary>
    [Fact]
    public void A_planted_background_caller_is_found_and_a_clean_source_is_not()
    {
        Assert.Equal(["BackgroundService"], MarketplaceOutboundScanner.BackgroundUses(PlantedMarketplaceSources.BackgroundCaller));
        Assert.Empty(MarketplaceOutboundScanner.BackgroundUses(PlantedMarketplaceSources.Clean));
        Assert.Equal(["new Timer("], MarketplaceOutboundScanner.BackgroundUses("var t = new Timer(Tick);"));
    }

    /// <summary>The comment remover leaves code on the same line as a URL alone and removes real comments.</summary>
    [Fact]
    public void Comments_are_removed_and_urls_are_not_taken_for_comments()
    {
        var source = "var url = \"https://x.test\"; var c = new HttpClient(); // trailing\n/* HttpClientHandler */";

        var stripped = MarketplaceOutboundScanner.WithoutComments(source);

        Assert.Contains("new HttpClient()", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("trailing", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClientHandler", stripped, StringComparison.Ordinal);
    }
}
