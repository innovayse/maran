using Maran.Modules.Licensing.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// An <see cref="ILicenceWriter"/> double that always fails — stands in for a write whose atomic
/// rename never completes (disk full, permission revoked mid-operation), so a test can assert the
/// previous licence artefact is left untouched by the handler when the write itself fails.
/// </summary>
internal sealed class ThrowingLicenceWriter : ILicenceWriter
{
    /// <inheritdoc />
    public Task InstallAsync(string rawLicenceText, CancellationToken cancellationToken)
    {
        throw new IOException("Simulated write failure: the atomic rename never completed.");
    }
}
