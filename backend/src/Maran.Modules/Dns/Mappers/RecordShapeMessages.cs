using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Resources;

namespace Maran.Modules.Dns.Mappers;

/// <summary>
/// Names the resource key that explains a shape verdict, for the validators. Each message is a bare resx
/// key and not an English sentence: the exception middleware forwards a validation message only when it is
/// entirely alphanumeric, and then resolves it as an error code against the module's resources.
/// </summary>
public static class RecordShapeMessages
{
    /// <summary>Maps a verdict that is not <see cref="RecordShapeVerdict.Accepted"/> to its resource key.</summary>
    /// <param name="verdict">The shape policy's verdict.</param>
    /// <returns>The key, which is also the machine-stable error code.</returns>
    public static string KeyOf(RecordShapeVerdict verdict)
    {
        return verdict switch
        {
            RecordShapeVerdict.NameInvalid => nameof(ErrorMessages.RecordNameInvalid),
            RecordShapeVerdict.TtlOutOfRange => nameof(ErrorMessages.RecordTtlOutOfRange),
            RecordShapeVerdict.ValueInvalid => nameof(ErrorMessages.RecordValueInvalid),
            RecordShapeVerdict.PriorityOutOfRange => nameof(ErrorMessages.RecordPriorityOutOfRange),
            RecordShapeVerdict.WeightOutOfRange => nameof(ErrorMessages.RecordWeightOutOfRange),
            RecordShapeVerdict.PortOutOfRange => nameof(ErrorMessages.RecordPortOutOfRange),
            RecordShapeVerdict.FlagsInvalid => nameof(ErrorMessages.RecordFlagsInvalid),
            RecordShapeVerdict.TagInvalid => nameof(ErrorMessages.RecordTagInvalid),
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "An accepted record has no message."),
        };
    }
}
