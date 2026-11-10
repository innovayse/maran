using Maran.Sdk.Filters;
using Microsoft.AspNetCore.Mvc;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The controller of a PAID module, standing in for the first one the panel will sell: its only mark is
/// <see cref="RequiresLicensedModuleAttribute"/> for <see cref="ModuleId"/>, which no licence ever names
/// unless the test installs one that does.
/// </summary>
/// <remarks>
/// It exists because no module the panel ships is paid, so the request-time gate would otherwise be
/// observed only by unit tests of the filter and never on the wire. It is added to a host by the one test
/// class that needs it (<see cref="LicensedModuleGateTests"/>) through an application part, and to no
/// other, so the route-table censuses never see it.
/// </remarks>
[ApiController]
[Route(RoutePath)]
[RequiresLicensedModule(ModuleId)]
public sealed class LicensedProbeController : ControllerBase
{
    /// <summary>The id of the module this probe pretends to belong to.</summary>
    public const string ModuleId = "licensed-probe";

    /// <summary>The probe's route.</summary>
    public const string RoutePath = "api/v1/test-probes/licensed";

    /// <summary>Answers 200 to whoever the pipeline and the licence gate let through.</summary>
    /// <returns>An empty 200.</returns>
    [HttpGet]
    public IActionResult Get()
    {
        return Ok();
    }
}
