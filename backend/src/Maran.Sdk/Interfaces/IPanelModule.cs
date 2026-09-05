using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// The contract every panel module implements — internal v1 modules and
/// marketplace modules share this exact shape (spec §13). Grows additively.
/// </summary>
public interface IPanelModule
{
    /// <summary>Stable machine name; equals the PostgreSQL schema name.</summary>
    string Name { get; }

    /// <summary>
    /// This module's published identity: id, display-name resource key, version, licence tier, and
    /// dependencies. The Host's modules catalogue (<c>GET /api/v1/modules</c>) reads this directly
    /// rather than each module publishing ad hoc — a module is discovered at runtime, so this is
    /// the only place the SPA and the licence system can learn what it is.
    /// </summary>
    Manifest Manifest { get; }

    /// <summary>Registers the module's services and options.</summary>
    /// <param name="services">The Host's DI container to add the module's services to.</param>
    /// <param name="configuration">The Host's configuration, for reading module-specific settings.</param>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
