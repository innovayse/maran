using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="ICorrelationIdAccessor"/> double reporting a fixed id, so a test can assert that an
/// instrumented handler puts the REQUEST's id on the task it opens rather than inventing one.
/// </summary>
public sealed class StubCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <summary>Creates the accessor.</summary>
    /// <param name="correlationId">The id it reports; null stands for work with no request behind it.</param>
    public StubCorrelationIdAccessor(string? correlationId)
    {
        CorrelationId = correlationId;
    }
}
