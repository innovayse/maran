using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Utilities.Network;

/// <summary>The facts of the machine the process runs on, read from the operating system on every call.</summary>
public sealed class SystemThisMachineFactsSource : IThisMachineFactsSource
{
    /// <inheritdoc />
    public ThisMachineFacts Read()
    {
        return ThisMachineFacts.Read();
    }
}
