using Maran.ArchitectureTests.Fixtures;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the Licensing module's outbound call to the one place rules/security.md item 10 (fifth exception) names:
/// one client type reaches the network, one extension registers the named client, the module's only unattended work
/// is the three types that are meant to run on their own, and nothing but the renewal writes the lease file.
/// </summary>
/// <remarks>
/// <para>
/// Reuses <see cref="MarketplaceOutboundScanner"/>, which is not specific to the marketplace: it reads source text
/// without comments for whole-word network and background types. Its blind spots are its own, stated there: a client
/// reached through reflection or a name built at run time, and a type in another assembly that creates a client on this
/// module's behalf (the Host's pipeline registration, which is the Host's).
/// </para>
/// <para>
/// Every refusing test is paired with a check that the scan can see what it claims to: the client's own file must
/// show its network types, the planted sources must be found and the clean one must not, so a scan that stopped
/// reading would fail loudly instead of passing by finding nothing.
/// </para>
/// </remarks>
public sealed class LicensingOutboundIsGatedTests
{
    /// <summary>The one file that may touch an HTTP client.</summary>
    private const string ClientFile = "Services/LeaseServiceClient.cs";

    /// <summary>The one file that may register the named client.</summary>
    private const string RegistrationFile = "Extensions/LeaseServiceClientExtensions.cs";

    /// <summary>The module has well over this many source files; fewer means the scan read the wrong folder.</summary>
    private const int LeastSourceFiles = 50;

    /// <summary>The files the module's work of its own may live in: the renewal, the watcher and the start-up probe.</summary>
    private static readonly string[] BackgroundFiles =
    [
        "Services/LeaseRenewalWorker.cs",
        "Services/LicenceCoverageWatcher.cs",
        "Services/LicenceStartupCheck.cs",
    ];

    /// <summary>The files that may name the writer of the lease file.</summary>
    private static readonly string[] LeaseWriterFiles =
    [
        "Interfaces/ILeaseWriter.cs",
        "LicensingModule.cs",
        "Services/FileLeaseWriter.cs",
        "Services/LeaseRenewer.cs",
    ];

    /// <summary>The module's folder.</summary>
    /// <returns>Its absolute path.</returns>
    private static string ModuleFolder()
    {
        return Path.Combine(ResourceTriples.BackendRoot(), "src", "Maran.Modules", "Licensing");
    }

    /// <summary>A path inside the module, written with forward slashes.</summary>
    /// <param name="path">An absolute path inside the module.</param>
    /// <returns>The path relative to the module.</returns>
    private static string Relative(string path)
    {
        return Path.GetRelativePath(ModuleFolder(), path).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Only the lease service client touches an HTTP client.</summary>
    [Fact]
    public void Only_the_lease_service_client_touches_an_http_client()
    {
        var offenders = MarketplaceOutboundScanner.SourceFiles(ModuleFolder())
            .Where(file => { return Relative(file) != ClientFile; })
            .Select(file => { return (Name: Relative(file), Uses: MarketplaceOutboundScanner.NetworkUses(File.ReadAllText(file))); })
            .Where(found => { return found.Uses.Count > 0; })
            .Select(found => { return $"{found.Name}: {string.Join(", ", found.Uses)}"; })
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Only {ClientFile} may touch an HTTP client in the Licensing module (rules/security.md item 10):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>The scan reads the module, and sees the client's types in the client's file.</summary>
    [Fact]
    public void The_scan_reads_the_module_and_sees_the_clients_types()
    {
        var files = MarketplaceOutboundScanner.SourceFiles(ModuleFolder());
        var client = File.ReadAllText(Path.Combine(ModuleFolder(), ClientFile.Replace('/', Path.DirectorySeparatorChar)));

        Assert.True(files.Count >= LeastSourceFiles, $"The scan read {files.Count} files; the module has more than that.");
        Assert.Contains("IHttpClientFactory", MarketplaceOutboundScanner.NetworkUses(client));
        Assert.Contains("HttpClient", MarketplaceOutboundScanner.NetworkUses(client));
        Assert.Contains("SocketsHttpHandler", MarketplaceOutboundScanner.NetworkUses(client));
    }

    /// <summary>A planted use of an HTTP client is found and a clean source is not: the scan can fail.</summary>
    [Fact]
    public void A_planted_use_of_an_http_client_is_found_and_a_clean_source_is_not()
    {
        Assert.Equal(["HttpClient"], MarketplaceOutboundScanner.NetworkUses(PlantedMarketplaceSources.OwnHttpClient));
        Assert.Empty(MarketplaceOutboundScanner.NetworkUses(PlantedMarketplaceSources.Clean));
    }

    /// <summary>The named client is registered by the one extension and nowhere else.</summary>
    [Fact]
    public void The_named_client_is_registered_only_by_the_registration_extension()
    {
        var registering = MarketplaceOutboundScanner.SourceFiles(ModuleFolder())
            .Where(file => { return MarketplaceOutboundScanner.WithoutComments(File.ReadAllText(file)).Contains(".AddHttpClient(", StringComparison.Ordinal); })
            .Select(Relative)
            .ToList();

        Assert.Equal([RegistrationFile], registering);
    }

    /// <summary>The module runs work of its own only in the three types that are meant to.</summary>
    [Fact]
    public void Only_the_renewal_the_watcher_and_the_start_up_probe_run_on_their_own()
    {
        var running = MarketplaceOutboundScanner.SourceFiles(ModuleFolder())
            .Where(file => { return MarketplaceOutboundScanner.BackgroundUses(File.ReadAllText(file)).Count > 0; })
            .Select(Relative)
            .ToList();

        Assert.Equal(BackgroundFiles, running);
    }

    /// <summary>A planted background caller is found and a clean source is not: the scan can fail.</summary>
    [Fact]
    public void A_planted_background_caller_is_found_and_a_clean_source_is_not()
    {
        Assert.Equal(["BackgroundService"], MarketplaceOutboundScanner.BackgroundUses(PlantedMarketplaceSources.BackgroundCaller));
        Assert.Empty(MarketplaceOutboundScanner.BackgroundUses(PlantedMarketplaceSources.Clean));
    }

    /// <summary>Nothing but the renewal (and the writer it uses) names the writer of the lease file.</summary>
    [Fact]
    public void Only_the_renewal_names_the_writer_of_the_lease_file()
    {
        var naming = MarketplaceOutboundScanner.SourceFiles(ModuleFolder())
            .Where(file => { return MarketplaceOutboundScanner.WithoutComments(File.ReadAllText(file)).Contains("ILeaseWriter", StringComparison.Ordinal); })
            .Select(Relative)
            .ToList();

        Assert.Equal(LeaseWriterFiles, naming);
    }
}
