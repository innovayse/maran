namespace Maran.Modules.Dns.Domain.Enums;

/// <summary>The answer to "is this record well-formed on its own", before it is weighed against its zone.</summary>
public enum RecordShapeVerdict
{
    /// <summary>The record is well-formed.</summary>
    Accepted,

    /// <summary>The owner name is not <c>@</c> or a run of valid labels that fits the zone.</summary>
    NameInvalid,

    /// <summary>The time to live is outside the allowed bounds.</summary>
    TtlOutOfRange,

    /// <summary>The value is not a valid address, host name or text for the record's type.</summary>
    ValueInvalid,

    /// <summary>The priority or preference is outside 0 to 65535, or is set on a type that has none.</summary>
    PriorityOutOfRange,

    /// <summary>The weight is outside 0 to 65535, or is set on a type that has none.</summary>
    WeightOutOfRange,

    /// <summary>The port is outside 0 to 65535, or is set on a type that has none.</summary>
    PortOutOfRange,

    /// <summary>The flags are not a value the type accepts, or are set on a type that has none.</summary>
    FlagsInvalid,

    /// <summary>The tag is not one the type accepts, or is set on a type that has none.</summary>
    TagInvalid,
}
