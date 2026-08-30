using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// An <see cref="ICorrelationIdAccessor"/> double reporting a fixed id, or none at all — the case
/// that happens outside a request, such as in a background job.
/// </summary>
public sealed class FakeCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <inheritdoc />
    public string? CorrelationId { get; }

    /// <summary>Creates an accessor reporting <paramref name="correlationId"/>.</summary>
    /// <param name="correlationId">The id to report, or null for "outside a request".</param>
    public FakeCorrelationIdAccessor(string? correlationId)
    {
        CorrelationId = correlationId;
    }
}
