using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// Decides whether one record may be part of a zone template. The tokens go through
/// <see cref="TemplateTokenRules"/>; then the record is expanded against <see cref="TemplateContext.Sample"/>
/// and the result goes through <see cref="RecordShapePolicy"/>, the same law a customer's own record passes, so
/// a template can place into a zone nothing that a customer's record could not.
/// </summary>
public static class TemplateRecordRules
{
    /// <summary>Weighs one normalised template record.</summary>
    /// <param name="record">A record already passed through <see cref="TemplateExpansionPolicy.Normalize"/>.</param>
    /// <returns>The verdict on the tokens and on the sample expansion.</returns>
    public static TemplateRecordVerdict Check(TemplateRecordDraft record)
    {
        var token = TemplateTokenRules.Check(record.Type, record.Value);
        if (token != TemplateTokenVerdict.Accepted)
        {
            return new TemplateRecordVerdict(token, RecordShapeVerdict.Accepted);
        }

        if (!TemplateExpansionPolicy.TryExpand([record], TemplateContext.Sample, out var expansion))
        {
            return new TemplateRecordVerdict(TemplateTokenVerdict.Unbalanced, RecordShapeVerdict.Accepted);
        }

        var shape = RecordShapePolicy.Check(
            RecordShapePolicy.Normalize(expansion!.Drafts.Single()),
            TemplateContext.Sample.Apex);

        return new TemplateRecordVerdict(TemplateTokenVerdict.Accepted, shape);
    }
}
