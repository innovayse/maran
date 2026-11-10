using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Domain.ValueObjects;

/// <summary>
/// The answer to "may this record be part of a template": what the token rules said and, when they accepted
/// the value, what the shape policy said about the record the sample context expands it to.
/// </summary>
/// <param name="Token">The token rules' verdict on the record's value.</param>
/// <param name="Shape">The shape policy's verdict on the sample expansion; accepted when the tokens were not.</param>
public readonly record struct TemplateRecordVerdict(TemplateTokenVerdict Token, RecordShapeVerdict Shape)
{
    /// <summary>True when both the tokens and the sample expansion were accepted.</summary>
    public bool IsAccepted
    {
        get
        {
            return Token == TemplateTokenVerdict.Accepted && Shape == RecordShapeVerdict.Accepted;
        }
    }
}
