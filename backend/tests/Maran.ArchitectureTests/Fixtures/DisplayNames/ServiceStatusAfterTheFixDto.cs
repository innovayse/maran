using Maran.Agent.Client.Services.MonitorService;

namespace Maran.ArchitectureTests.Fixtures.DisplayNames;

/// <summary>
/// The second inverse control: the monitoring row as it was fixed, where the display name of the
/// subject is called <c>Name</c> and the law must accept it.
/// </summary>
/// <remarks>
/// It exists because the subject rule is the one place the law accepts a bare <c>Name</c>, and a
/// rule with no test that exercises its accepting side is a rule that can be tightened into
/// refusing everything without anything going red.
/// </remarks>
/// <param name="Service">Which service this row describes, machine-stable.</param>
/// <param name="Name">The same service as an operator reads it.</param>
/// <param name="Detail">Why, in the service manager's own words.</param>
public sealed record ServiceStatusAfterTheFixDto(AgentManagedService Service, string Name, string Detail);
