using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// Turns a zone template's records into the drafts one zone is created with. A pure function of the records
/// and a <see cref="TemplateContext"/>: it reads no clock, no database and no setting, so the same template
/// expands the same way when it is checked at save and when it is applied at creation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Expansion produces drafts and decides nothing about them.</b> Every draft still has to pass
/// <see cref="RecordShapePolicy"/> and <see cref="ZoneRecordRules"/>, the same law a customer's own record
/// passes, and the callers run that: at save against <see cref="TemplateContext.Sample"/> and at creation
/// against the real context. That is why a template opens no second path for a value into a zone file.
/// </para>
/// <para>
/// <b>A record whose variable has no value is skipped and reported, never completed.</b> No IPv6 address in the
/// settings means no AAAA record; the panel does not invent an address.
/// </para>
/// </remarks>
public static class TemplateExpansionPolicy
{
    /// <summary>
    /// Gives a template record its stored spelling: a lower-case trimmed name and tag, and a value that is never
    /// null. The value is otherwise left as written, because a text value is stored as it was typed.
    /// </summary>
    /// <param name="record">The record as stated.</param>
    /// <returns>The normalised record. Normalising decides nothing; the rules do.</returns>
    public static TemplateRecordDraft Normalize(TemplateRecordDraft record)
    {
        return record with
        {
            Name = RecordNamePolicy.Normalize(record.Name),
            Value = record.Value ?? string.Empty,
            Tag = (record.Tag ?? string.Empty).Trim().ToLowerInvariant(),
        };
    }

    /// <summary>Expands the records of a template for one zone.</summary>
    /// <param name="records">The template's records, in order.</param>
    /// <param name="context">The values of the variables for this zone.</param>
    /// <param name="expansion">The drafts and the skipped records; null when the method returns false.</param>
    /// <returns>
    /// True when every record's variables are accepted by <see cref="TemplateTokenRules"/>; false when any record
    /// is not, in which case nothing is expanded at all, so a half-applied template cannot occur.
    /// </returns>
    public static bool TryExpand(
        IReadOnlyList<TemplateRecordDraft> records,
        TemplateContext context,
        out TemplateExpansion? expansion)
    {
        var drafts = new List<RecordDraft>();
        var skipped = new List<SkippedTemplateRecord>();

        foreach (var record in records)
        {
            if (TemplateTokenRules.Check(record.Type, record.Value) != TemplateTokenVerdict.Accepted)
            {
                expansion = null;
                return false;
            }

            var missing = TemplateTokenRules.VariablesIn(record.Value)
                .Where(variable =>
                {
                    return context.ValueOf(variable).Length == 0;
                })
                .Select(variable =>
                {
                    return (TemplateVariable?)variable;
                })
                .FirstOrDefault();
            if (missing is not null)
            {
                skipped.Add(new SkippedTemplateRecord(record.Name, record.Type, missing.Value));
                continue;
            }

            drafts.Add(new RecordDraft(
                record.Name,
                record.Type,
                record.Ttl ?? context.DefaultTtl,
                TemplateTokenRules.Substitute(record.Value, context),
                record.Priority,
                record.Weight,
                record.Port,
                record.Flags,
                record.Tag));
        }

        expansion = new TemplateExpansion(drafts, skipped);

        return true;
    }
}
