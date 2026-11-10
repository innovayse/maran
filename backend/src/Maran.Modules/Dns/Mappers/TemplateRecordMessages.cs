using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Resources;

namespace Maran.Modules.Dns.Mappers;

/// <summary>
/// Names the resource key that explains a refused template record, for the validators and the handler. A token
/// fault has keys of its own; a shape fault is the very key a customer's record gets for the same fault.
/// </summary>
public static class TemplateRecordMessages
{
    /// <summary>Maps a verdict that is not accepted to its resource key.</summary>
    /// <param name="verdict">The template record rules' verdict.</param>
    /// <returns>The key, which is also the machine-stable error code.</returns>
    public static string KeyOf(TemplateRecordVerdict verdict)
    {
        return verdict.Token switch
        {
            TemplateTokenVerdict.Unbalanced => nameof(ErrorMessages.ZoneTemplateTokenUnbalanced),
            TemplateTokenVerdict.UnknownToken => nameof(ErrorMessages.ZoneTemplateTokenUnknown),
            TemplateTokenVerdict.NotAllowedHere => nameof(ErrorMessages.ZoneTemplateTokenNotAllowed),
            _ => RecordShapeMessages.KeyOf(verdict.Shape),
        };
    }
}
