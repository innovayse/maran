using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.ValueObjects;

namespace Maran.Modules.Sites.Mappers;

/// <summary>Restates a <see cref="PhpLifecycleReading"/> as the outward <see cref="PhpLifecycleDto"/>.</summary>
/// <remarks>
/// A mapper translates and never decides: the reading arrives already made by <c>PhpLifecycle</c>, and
/// the localized status word arrives from the caller's resolver, so this type holds neither the table
/// nor a resource lookup.
/// </remarks>
public static class PhpLifecycleMapper
{
    /// <summary>Restates a reading on the wire.</summary>
    /// <param name="reading">What the lifecycle table says about the version on the day.</param>
    /// <param name="statusName">The reading's status as the reader's language names it.</param>
    /// <returns>The same facts in the outward shape.</returns>
    public static PhpLifecycleDto From(PhpLifecycleReading reading, string statusName)
    {
        return new PhpLifecycleDto(reading.Status, statusName, reading.EndOfLife, reading.DaysLeft);
    }
}
