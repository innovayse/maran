using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// Which optional fields each record type uses. The one statement of it: the shape policy refuses a field
/// a type does not use, and the reference data a form is drawn from is read from the same answers, so the
/// form never offers a field the server would refuse.
/// </summary>
public static class RecordTypeFields
{
    /// <summary>Tells whether the type carries a priority or preference: MX and SRV.</summary>
    /// <param name="type">The record type.</param>
    /// <returns>True when <c>Priority</c> is meaningful for the type.</returns>
    public static bool UsesPriority(RecordType type)
    {
        return type is RecordType.Mx or RecordType.Srv;
    }

    /// <summary>Tells whether the type carries a weight and a port: SRV.</summary>
    /// <param name="type">The record type.</param>
    /// <returns>True when <c>Weight</c> and <c>Port</c> are meaningful for the type.</returns>
    public static bool UsesWeightAndPort(RecordType type)
    {
        return type == RecordType.Srv;
    }

    /// <summary>Tells whether the type carries flags and a tag: CAA.</summary>
    /// <param name="type">The record type.</param>
    /// <returns>True when <c>Flags</c> and <c>Tag</c> are meaningful for the type.</returns>
    public static bool UsesFlagsAndTag(RecordType type)
    {
        return type == RecordType.Caa;
    }
}
