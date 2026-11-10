using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Marketplace.Tests.TestSupport;

/// <summary>A correlation id accessor that always answers the same id.</summary>
public sealed class FixedCorrelationIdAccessor : ICorrelationIdAccessor
{
    /// <inheritdoc />
    public string? CorrelationId { get; } = "correlation-1";
}
