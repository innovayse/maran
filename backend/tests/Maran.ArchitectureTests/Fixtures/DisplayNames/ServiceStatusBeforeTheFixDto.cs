using Maran.Agent.Client.Services.MonitorService;

namespace Maran.ArchitectureTests.Fixtures.DisplayNames;

/// <summary>
/// The monitoring screen as it shipped: the service's identity reached the badges as the enum
/// member's own name, so an operator read <c>webServer</c>, <c>database</c>, <c>cron</c> and
/// <c>ssh</c> down a column of an otherwise translated page.
/// </summary>
/// <remarks>
/// Two members are flagged and only one of them was a defect, which is what the exemption register
/// is for. <c>Service</c> is an inventory of things the agent watches — it grows with the agent,
/// and the panel is the only side that can name the new one, so the fix put a localized
/// <c>Name</c> beside it. <c>State</c> is three badge tones, and the real
/// <c>ServiceStatusDto</c> is excused for it against <c>monitoring.services.*</c>, which the law
/// resolves in English, Russian and Armenian rather than believing.
/// </remarks>
/// <param name="Service">Which service this row describes.</param>
/// <param name="State">Up, down, or not known.</param>
/// <param name="Detail">Why, in the service manager's own words.</param>
public sealed record ServiceStatusBeforeTheFixDto(AgentManagedService Service, AgentServiceState State, string Detail);
