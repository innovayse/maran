namespace Maran.Agent.Simulated.State;

/// <summary>A PHP version installed on the simulated host, with its FPM runtime.</summary>
/// <param name="Version">The two-component version, such as <c>8.3</c>; the runtime's key.</param>
/// <param name="FpmSocketDirectory">The directory the version's FPM sockets are created in.</param>
/// <param name="IsDefault">Whether this is the host's default command-line PHP.</param>
/// <param name="EnabledExtensions">The catalogue extensions this version's php-fpm has loaded.</param>
public sealed record SimulatedPhpRuntime(
    string Version,
    string FpmSocketDirectory,
    bool IsDefault,
    IReadOnlyList<string> EnabledExtensions);
