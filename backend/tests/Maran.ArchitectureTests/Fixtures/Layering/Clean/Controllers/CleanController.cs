using Maran.ArchitectureTests.Fixtures.Layering.Clean.Commands;
using Maran.ArchitectureTests.Fixtures.Layering.Clean.Domain;

namespace Maran.ArchitectureTests.Fixtures.Layering.Clean.Controllers;

/// <summary>
/// A clean controller that depends on a command and a Domain type, both allowed: the accepting control for the
/// Controllers rule.
/// </summary>
public sealed class CleanController
{
    /// <summary>The command the controller dispatches.</summary>
    public CleanLayerCommand? Command { get; init; }

    /// <summary>The Domain type the controller reads.</summary>
    public CleanDomainEntity? Entity { get; init; }
}
