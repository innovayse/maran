using Maran.Host.Extensions;
using Maran.Host.OpenApi;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.OpenApi.Fixtures;

/// <summary>
/// Two separately booted panel hosts, each with the OpenAPI generator registered, and the document each
/// one renders. Two hosts and not one render twice, because the order the generator meets controllers in
/// is decided at boot: a second render from the same container would repeat the same accident.
/// </summary>
public sealed class OpenApiDocumentFixture : IDisposable
{
    /// <summary>The roots the two hosts derive from, kept so they are disposed with them.</summary>
    private readonly List<IDisposable> _owned = [];

    /// <summary>The document rendered by the first host.</summary>
    public string First { get; }

    /// <summary>The document rendered by the second host.</summary>
    public string Second { get; }

    /// <summary>The first host's writer, the object the verb writes through.</summary>
    public OpenApiDocumentWriter Writer { get; }

    /// <summary>Every API description the first host's controllers contribute.</summary>
    public IReadOnlyList<ApiDescription> Descriptions { get; }

    /// <summary>Boots both hosts and renders from each.</summary>
    public OpenApiDocumentFixture()
    {
        var first = Boot();
        var second = Boot();

        Writer = first.Services.GetRequiredService<OpenApiDocumentWriter>();
        First = Writer.Render();
        Second = second.Services.GetRequiredService<OpenApiDocumentWriter>().Render();
        Descriptions = first.Services
            .GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items
            .SelectMany(group => { return group.Items; })
            .ToList();
    }

    /// <summary>Boots one hermetic panel host with the generator registered.</summary>
    /// <returns>The booted host's factory.</returns>
    private WebApplicationFactory<Program> Boot()
    {
        var root = new PanelTestFactory();
        var host = root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => { services.AddPanelOpenApiGenerator(); });
        });

        _owned.Add(host);
        _owned.Add(root);
        return host;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var owned in _owned)
        {
            owned.Dispose();
        }
    }
}
