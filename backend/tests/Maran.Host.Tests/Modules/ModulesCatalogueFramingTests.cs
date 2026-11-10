using System.Text.Json;
using Maran.Host.Modules;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// What the catalogue says about a packaged module's frame and failure, with no database: a packaged
/// module composed into the host, the state the loader reports for it, and the bundle window's answer.
/// Pairs each refusal with the accepting case it is about.
/// </summary>
public sealed class ModulesCatalogueFramingTests : IClassFixture<PanelTestFactory>
{
    /// <summary>The id of the packaged module under test.</summary>
    private const string Id = "hello";

    /// <summary>
    /// A failure code the Sdk's shipped table claims in three languages: the open panel's loader refusing
    /// a change.
    /// </summary>
    private const string KnownCause = "MarketplaceNotAvailable";

    /// <summary>A well-formed integrity value for the stub bundle.</summary>
    private static readonly string Integrity = "sha384-" + Convert.ToBase64String(new byte[48]);

    /// <summary>The booted host factory the per-test hosts derive from.</summary>
    private readonly PanelTestFactory _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    /// <param name="factory">The hermetic host factory.</param>
    public ModulesCatalogueFramingTests(PanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>A packaged module the loader reports nothing about is active and framed.</summary>
    [Fact]
    public async Task A_packaged_module_the_loader_does_not_mention_is_active_and_framed()
    {
        using var catalogue = await ReadAsync(held: null, bundle: BundleOf("1.0.0"));
        var module = ModuleOf(catalogue);

        Assert.Equal("active", module.GetProperty("lifecycle").GetString());
        Assert.Equal("/module-frames/hello/1.0.0/", module.GetProperty("spa").GetProperty("frameUrl").GetString());
        Assert.Equal(Integrity, module.GetProperty("spa").GetProperty("integrity").GetString());
    }

    /// <summary>A degraded module whose cause a table claims is described in the reader's language, with no frame.</summary>
    /// <param name="culture">The reader's language.</param>
    /// <param name="mustContain">A word the sentence in that language holds.</param>
    [Theory]
    [InlineData("en", "packages")]
    [InlineData("ru", "пакетов")]
    [InlineData("hy", "Փաթեթներից")]
    public async Task A_degraded_module_with_a_known_cause_is_described_in_the_readers_language_and_not_framed(
        string culture,
        string mustContain)
    {
        var cause = Error.Of(KnownCause, ErrorType.Unavailable);
        using var catalogue = await ReadAsync(
            held: new LoadedModuleInfo(Id, "1.0.0", ModuleLifecycleState.Degraded, "test", cause),
            bundle: BundleOf("1.0.0"),
            culture: culture);
        var module = ModuleOf(catalogue);

        Assert.Equal("degraded", module.GetProperty("lifecycle").GetString());
        Assert.Equal(JsonValueKind.Null, module.GetProperty("spa").ValueKind);
        var sentence = module.GetProperty("failureDisplay").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sentence));
        Assert.NotEqual(cause.Code, sentence);
        Assert.Contains(mustContain, sentence!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A cause no table claims is not shown as its own machine code.</summary>
    [Fact]
    public async Task A_failure_cause_no_table_claims_is_not_shown_as_its_machine_code()
    {
        using var catalogue = await ReadAsync(
            held: new LoadedModuleInfo(
                Id, "1.0.0", ModuleLifecycleState.Failed, "test", Error.Of("NobodyDefinedThis", ErrorType.Failure)),
            bundle: BundleOf("1.0.0"));
        var module = ModuleOf(catalogue);

        Assert.Equal("failed", module.GetProperty("lifecycle").GetString());
        Assert.Equal(JsonValueKind.Null, module.GetProperty("failureDisplay").ValueKind);
    }

    /// <summary>The same module reported active by the loader carries no failure sentence.</summary>
    [Fact]
    public async Task An_active_module_carries_no_failure_sentence()
    {
        using var catalogue = await ReadAsync(
            held: new LoadedModuleInfo(Id, "1.0.0", ModuleLifecycleState.Installed, "test", null),
            bundle: BundleOf("1.0.0"));

        Assert.Equal(JsonValueKind.Null, ModuleOf(catalogue).GetProperty("failureDisplay").ValueKind);
    }

    /// <summary>Every module that shipped with the panel reports a version, an active state, no packaging and no frame.</summary>
    [Fact]
    public async Task Every_shipped_module_reports_its_version_active_unpackaged_and_unframed()
    {
        using var catalogue = await ReadAsync(held: null, bundle: BundleOf("1.0.0"));
        var shipped = catalogue.RootElement.EnumerateArray()
            .Where(module => { return module.GetProperty("name").GetString() != Id; })
            .ToList();

        Assert.NotEmpty(shipped);
        foreach (var module in shipped)
        {
            var manifest = ModuleRegistry.All
                .Single(candidate => { return candidate.Manifest.Id == module.GetProperty("name").GetString(); })
                .Manifest;

            Assert.Equal(manifest.Version, module.GetProperty("version").GetString());
            Assert.Equal("active", module.GetProperty("lifecycle").GetString());
            Assert.False(module.GetProperty("isPackaged").GetBoolean());
            Assert.Equal(JsonValueKind.Null, module.GetProperty("spa").ValueKind);
        }
    }

    /// <summary>Describes a bundle of a version.</summary>
    /// <param name="version">The version the bundle is for.</param>
    /// <returns>The description.</returns>
    private static ModuleSpaDescription BundleOf(string version)
    {
        return new ModuleSpaDescription(version, "entry.js", Integrity, ["en"]);
    }

    /// <summary>Reads the catalogue from a host with one packaged module composed.</summary>
    /// <param name="held">What the loader reports for the module, or null for nothing.</param>
    /// <param name="bundle">What the bundle window reports.</param>
    /// <param name="culture">The <c>Accept-Language</c> value.</param>
    /// <returns>The parsed document; the caller disposes it.</returns>
    private async Task<JsonDocument> ReadAsync(LoadedModuleInfo? held, ModuleSpaDescription bundle, string culture = "en")
    {
        // Built, not composed: the composer's capability guard reads every type of the stand-in's assembly,
        // which is this test assembly and holds agent fakes, so it would refuse the module under test. What
        // admission does is the composer's own tests' business.
        var packaged = new StubPackagedModule(TestManifests.Of(Id));
        var composition = new PackagedModuleComposition([.. ModuleRegistry.All, packaged], [packaged], []);
        var loader = held is null ? new StubModuleLoader() : new StubModuleLoader(held);

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(composition);
                services.AddSingleton<IModuleLoader>(loader);
                services.AddSingleton<IModuleSpaAssets>(new StubModuleSpaAssets(bundle));
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", culture);

        var response = await client.GetAsync("/api/v1/modules");
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Finds the packaged module in a catalogue document.</summary>
    /// <param name="catalogue">The catalogue.</param>
    /// <returns>The module's element.</returns>
    private static JsonElement ModuleOf(JsonDocument catalogue)
    {
        return catalogue.RootElement.EnumerateArray()
            .Single(module => { return module.GetProperty("name").GetString() == Id; });
    }
}
