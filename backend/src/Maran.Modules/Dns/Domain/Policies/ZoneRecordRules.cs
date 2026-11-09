using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// The rules that weigh one record against the others in its zone, as opposed to
/// <see cref="RecordShapePolicy"/>, which weighs it alone.
/// </summary>
public static class ZoneRecordRules
{
    /// <summary>Weighs a normalised, well-formed draft against the records a zone holds.</summary>
    /// <param name="others">
    /// The zone's records the draft will sit beside. When a record is being changed, pass the zone's records
    /// without that one, so it is not compared with itself.
    /// </param>
    /// <param name="draft">The draft, already normalised and shape-checked.</param>
    /// <returns>
    /// <see cref="RecordRuleVerdict.Accepted"/>, or the first rule broken: a CNAME at the apex, an apex name
    /// server, a CNAME beside other data (or other data beside a CNAME) at one name, or an exact duplicate.
    /// </returns>
    public static RecordRuleVerdict Check(IReadOnlyCollection<ZoneRecord> others, RecordDraft draft)
    {
        if (draft.Type == RecordType.Cname && draft.Name == RecordNamePolicy.Apex)
        {
            return RecordRuleVerdict.CnameAtApex;
        }

        if (draft.Type == RecordType.Ns && draft.Name == RecordNamePolicy.Apex)
        {
            return RecordRuleVerdict.NsAtApex;
        }

        var sameName = others.Where(record =>
        {
            return record.Name == draft.Name;
        }).ToList();
        if (draft.Type == RecordType.Cname && sameName.Count > 0)
        {
            return RecordRuleVerdict.CnameBesideOtherData;
        }

        if (draft.Type != RecordType.Cname && sameName.Any(record =>
        {
            return record.Type == RecordType.Cname;
        }))
        {
            return RecordRuleVerdict.OtherDataBesideCname;
        }

        return sameName.Any(record =>
        {
            return record.Holds(draft);
        }) ? RecordRuleVerdict.Duplicate : RecordRuleVerdict.Accepted;
    }
}
