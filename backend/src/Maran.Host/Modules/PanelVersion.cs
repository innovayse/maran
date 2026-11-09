using Maran.Host.OpenApi;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Modules;

/// <summary>
/// The version of the panel the modules are composed into, read from the assembly the panel is built
/// from. The Sdk's <see cref="IPanelVersion"/> is the read-only window a compatibility check asks.
/// </summary>
/// <remarks>
/// It states the same version the OpenAPI document does, without the source revision
/// (<see cref="OpenApiBuildVersion"/>), so the two can never disagree about which panel this is.
/// </remarks>
public sealed class PanelVersion : IPanelVersion
{
    /// <inheritdoc />
    public string Current
    {
        get { return OpenApiBuildVersion.Of(typeof(PanelVersion).Assembly); }
    }
}
