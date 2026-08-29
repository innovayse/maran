using Maran.SharedKernel.Interfaces;

namespace Maran.SharedKernel.Time;

/// <summary>
/// Production clock backed by the OS. This is the ONE place in the product allowed to read the
/// ambient clock: everything else injects <see cref="IClock"/> so time is substitutable in tests
/// (rules/csharp.md). The ban is analyzer-enforced, and the single suppression below is what makes
/// the rule honest — an implementation has to touch the real clock somewhere.
/// </summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
#pragma warning disable RS0030 // The clock implementation is the sanctioned exception to the ban.
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
#pragma warning restore RS0030
}
