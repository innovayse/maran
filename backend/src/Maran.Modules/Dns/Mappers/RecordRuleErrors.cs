using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Resources;

namespace Maran.Modules.Dns.Mappers;

/// <summary>Restates a zone's refusal of a record as the typed failure a caller is answered with.</summary>
public static class RecordRuleErrors
{
    /// <summary>Maps a verdict that is not <see cref="RecordRuleVerdict.Accepted"/> to its failure.</summary>
    /// <param name="verdict">The zone's verdict.</param>
    /// <returns>The error, code and kind together.</returns>
    public static Error From(RecordRuleVerdict verdict)
    {
        return verdict switch
        {
            RecordRuleVerdict.ZoneFull => Error.Of(nameof(ErrorMessages.ZoneRecordLimitReached), ErrorType.Conflict),
            RecordRuleVerdict.CnameBesideOtherData =>
                Error.Of(nameof(ErrorMessages.RecordCnameBesideOtherData), ErrorType.Conflict),
            RecordRuleVerdict.OtherDataBesideCname =>
                Error.Of(nameof(ErrorMessages.RecordOtherDataBesideCname), ErrorType.Conflict),
            RecordRuleVerdict.CnameAtApex => Error.Of(nameof(ErrorMessages.RecordCnameAtApex), ErrorType.Conflict),
            RecordRuleVerdict.NsAtApex => Error.Of(nameof(ErrorMessages.RecordNsAtApex), ErrorType.Conflict),
            RecordRuleVerdict.Duplicate => Error.Of(nameof(ErrorMessages.RecordDuplicate), ErrorType.Conflict),
            RecordRuleVerdict.RecordNotFound => Error.Of(nameof(ErrorMessages.RecordNotFound), ErrorType.NotFound),
            RecordRuleVerdict.RecordNotEditable =>
                Error.Of(nameof(ErrorMessages.RecordNotEditable), ErrorType.Conflict),
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "An accepted change has no error."),
        };
    }
}
