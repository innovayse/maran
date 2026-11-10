using Maran.Modules.Licensing.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>Counts how often the coverage memory was told to forget, standing in for the cache.</summary>
internal sealed class CountingInvalidator : ILicenceCoverageInvalidator
{
    /// <summary>How many times <see cref="Invalidate"/> was called.</summary>
    internal int Count { get; private set; }

    /// <inheritdoc />
    public void Invalidate()
    {
        Count++;
    }
}
