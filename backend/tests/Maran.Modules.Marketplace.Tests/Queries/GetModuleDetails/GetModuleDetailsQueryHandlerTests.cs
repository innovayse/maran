using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Queries.GetModuleDetails;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Queries.GetModuleDetails;

/// <summary>A module's details show every version with whether it can be installed, and the server's copy of the manifest summary.</summary>
public sealed class GetModuleDetailsQueryHandlerTests
{
    /// <summary>The query for <c>mail</c> as the controller stamps it.</summary>
    private static readonly GetModuleDetailsQuery Query = new("mail", "203.0.113.5", "test-agent");

    /// <summary>Builds the handler over the world.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The handler.</returns>
    private static GetModuleDetailsQueryHandler HandlerFor(OperationWorld world)
    {
        return new GetModuleDetailsQueryHandler(world.Client, world.Eligibility, world.Projector, world.Journal);
    }

    /// <summary>Each version carries its own installable flag and reason: a revoked one is not installable, a live one is.</summary>
    [Fact]
    public async Task Each_version_says_whether_it_can_be_installed()
    {
        using var world = new OperationWorld();
        world.Offer(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.2.0")),
            CatalogueSamples.Version("1.2.0"),
            CatalogueSamples.Version("1.1.0", revoked: true));

        var details = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        var live = details.Value.Versions.Single(version => { return version.Version == "1.2.0"; });
        var withdrawn = details.Value.Versions.Single(version => { return version.Version == "1.1.0"; });
        Assert.True(live.Installable);
        Assert.Null(live.NotInstallableMessage);
        Assert.False(withdrawn.Installable);
        Assert.True(withdrawn.Revoked);
        Assert.Equal(EchoErrorTextProvider.Prefix + ErrorCodes.PackageRevoked, withdrawn.NotInstallableMessage);
        Assert.Equal("mail", details.Value.Module.Id);
        Assert.Null(details.Value.Manifest);
    }

    /// <summary>The manifest summary is the server's copy, with the tier named in the caller's language and the declared items passed through.</summary>
    [Fact]
    public async Task The_manifest_summary_is_the_servers_copy()
    {
        using var world = new OperationWorld();
        var module = CatalogueSamples.Module("mail", CatalogueSamples.Version("1.2.0"));
        var manifest = new CatalogueManifestSummary(
            "Innovayse",
            "AddOn",
            "1.0.0",
            ["accounts"],
            [new CatalogueNamedItem("Firewall", "Firewall control")],
            [new CatalogueNamedItem("AdminOnly", "Administrator only")],
            "mail");
        world.Client.Details["mail"] = CatalogueSamples.Details(module, CatalogueSamples.Version("1.2.0")) with { Manifest = manifest };

        var details = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        var summary = details.Value.Manifest!;
        Assert.Equal("MarketplaceTierAddOn", summary.TierDisplayName);
        Assert.Equal("mail", summary.SchemaName);
        Assert.Equal("accounts", Assert.Single(summary.Dependencies));
        Assert.Equal("Firewall control", Assert.Single(summary.AgentCapabilities).DisplayName);
        Assert.Equal("AdminOnly", Assert.Single(summary.Permissions).Key);
    }

    /// <summary>A module the catalogue does not list is not found, and an answer that failed verification is journalled.</summary>
    [Fact]
    public async Task An_unknown_module_is_not_found_and_an_unverified_answer_is_journalled()
    {
        using var world = new OperationWorld();

        var unknown = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleNotInCatalogue, unknown.Error!.Code);
        Assert.Equal(ErrorType.NotFound, unknown.Error.Type);
        Assert.Empty(world.AuditFor(MarketplaceClientAuditActions.CatalogueVerificationFailed));

        world.Client.Failure = MarketplaceClientRefusals.CatalogueUnsigned;
        var unverified = await HandlerFor(world).HandleAsync(Query, CancellationToken.None);

        Assert.Equal(ErrorCodes.CatalogueUnsigned, unverified.Error!.Code);
        Assert.Single(world.AuditFor(MarketplaceClientAuditActions.CatalogueVerificationFailed));
    }
}
