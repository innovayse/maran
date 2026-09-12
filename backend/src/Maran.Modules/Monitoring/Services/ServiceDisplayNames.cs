using System.Text.Json;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Monitoring.Services;

/// <summary>
/// Resolves the operator-facing name of a watched service in the current request's culture.
/// </summary>
/// <remarks>
/// <para>
/// <b>The name is the backend's to produce, not the SPA's.</b> Every domain value the interface
/// shows is produced and localized here (rules/architecture.md "The backend owns the data, the SPA
/// renders it"); before this existed the services card printed the machine constant, so an operator
/// read <c>webServer</c> and <c>database</c> beside a translated status word. Built in the same
/// shape as the Tasks module's kind names and the Backups module's failure names, deliberately, so
/// the panel has one way of naming a machine-stable value rather than three.
/// </para>
/// <para>
/// <b>The key scheme lives in this one method and the input is a closed set.</b> Unlike a task
/// kind, which a marketplace module can record freely, every row's service is an
/// <see cref="AgentManagedService"/> member by construction: the agent client maps any wire value
/// it has no name for onto <see cref="AgentManagedService.Unspecified"/>, so this resolver can be
/// asked about nothing outside the enum — and the module's tests walk that enum and fail on the
/// first member without an entry.
/// </para>
/// <para>
/// <b>The fallback is the wire spelling, and the census test is what makes it unreachable.</b> A
/// member added to the enum without a resx entry would resolve to the same camelCase constant the
/// screen printed before this type existed — never the resx key, which names a file nobody outside
/// this repository can read. The camelCase form is chosen over the member's own PascalCase because
/// it is the one spelling the client already holds in the row's machine field, so a degraded name
/// at least matches the code beside it.
/// </para>
/// </remarks>
public sealed class ServiceDisplayNames
{
    /// <summary>The prefix a service's resx key carries, so services cannot collide with other names.</summary>
    private const string KeyPrefix = "Service";

    /// <summary>Resolves this module's display-name resources for the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the resolver.</summary>
    /// <param name="displayNames">This module's display-name resources.</param>
    public ServiceDisplayNames(IStringLocalizer<DisplayNames> displayNames)
    {
        _displayNames = displayNames;
    }

    /// <summary>Names one watched service as an operator reads it.</summary>
    /// <param name="service">The machine-stable service, as the agent client reported it.</param>
    /// <returns>
    /// The localized name, or the member's camelCase wire spelling when this build carries no entry
    /// for it — which the census test over the enum keeps from happening for anything that ships.
    /// </returns>
    public string Of(AgentManagedService service)
    {
        var localized = _displayNames[KeyPrefix + service];

        // The panel-wide enum converter's own naming policy, so the degraded name is byte-for-byte
        // the machine constant the row's `service` field carries on the wire.
        return localized.ResourceNotFound
            ? JsonNamingPolicy.CamelCase.ConvertName(service.ToString())
            : localized.Value;
    }
}
