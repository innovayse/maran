using Maran.SharedKernel.Utilities.Network;

namespace Maran.SharedKernel.Interfaces;

/// <summary>
/// Where the facts that name this machine come from, asked again at every use so an address added after
/// the process started (a second IP, a rotated temporary address) is known to the next check.
/// </summary>
public interface IThisMachineFactsSource
{
    /// <summary>Reads the facts as they are now.</summary>
    /// <returns>The facts; empty collections when the operating system refuses to say.</returns>
    ThisMachineFacts Read();
}
