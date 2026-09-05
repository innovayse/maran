using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Controllers;

namespace Maran.Host.IntegrationTests;

/// <summary>Behavioral contract of <see cref="ControllerRoutes"/>, which the IDOR fixtures rest on.</summary>
/// <remarks>
/// The fixtures assert their own completeness against this helper, so a defect here is silent in
/// exactly the wrong direction: a helper that thought no route was resource-scoped would let every
/// site- and certificate-scoped route pass with nothing proving it answers 404 rather than 403, while
/// the fixtures' documentation went on claiming otherwise.
/// </remarks>
public sealed class ControllerRoutesTests
{
    /// <summary>A route parameter makes a route resource scoped whatever the parameter is called.</summary>
    [Theory]
    [InlineData("GET /api/v1/sites/{id}", true)]
    [InlineData("GET /api/v1/sites/{siteId}", true)]
    [InlineData("POST /api/v1/sites/{id}/enable", true)]
    [InlineData("GET /api/v1/sites", false)]
    [InlineData("GET /api/v1/sites/php-versions", false)]
    public void A_route_parameter_makes_a_route_resource_scoped_whatever_the_parameter_is_called(
        string route,
        bool expected)
    {
        // Keyed on the shape, not on the literal "{id}": the previous spelling-based check would have
        // gone quiet for a route declared as {siteId:guid}.
        Assert.Equal(expected, ControllerRoutes.IsResourceScoped(route));
    }

    /// <summary>Declared routes are read off the controller with their constraints stripped.</summary>
    [Fact]
    public void Declared_routes_are_read_off_the_controller_with_their_constraints_stripped()
    {
        var declared = ControllerRoutes.Declared<SitesController>();

        Assert.Contains("GET /api/v1/sites/{id}", declared);
        Assert.Contains("GET /api/v1/sites/{id}/logs", declared);
        Assert.DoesNotContain(declared, route =>
        {
            return route.Contains(":guid", StringComparison.Ordinal);
        });
    }
}
