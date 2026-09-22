using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>A test <see cref="IClock"/> that always reads a fixed instant.</summary>
/// <param name="UtcNow">The instant this clock reads.</param>
internal sealed record FixedClock(DateTimeOffset UtcNow) : IClock;
