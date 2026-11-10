using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Entities;

/// <summary>
/// One record of a <see cref="ZoneTemplate"/>: the typed fields of a record whose value may name template
/// variables. It exists only inside its template, which creates and replaces it.
/// </summary>
/// <remarks>
/// Fields are typed columns, one meaning each, exactly as in <see cref="ZoneRecord"/>: a template is a second
/// author of record data and gets no free-text path that a customer's record does not have.
/// </remarks>
public sealed class ZoneTemplateRecord
{
    /// <summary>The row's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The template the record belongs to.</summary>
    public Guid ZoneTemplateId { get; private set; }

    /// <summary>The record's place in the template, so a template expands in the order it was written.</summary>
    public int Position { get; private set; }

    /// <summary>The owner name relative to the zone, lower case; <c>@</c> is the apex.</summary>
    public string Name { get; private set; }

    /// <summary>The record type.</summary>
    public RecordType Type { get; private set; }

    /// <summary>Time to live in seconds, or null for the settings' default when a zone is created.</summary>
    public int? Ttl { get; private set; }

    /// <summary>The address, target host name, text or CAA value, possibly naming variables.</summary>
    public string Value { get; private set; }

    /// <summary>MX preference or SRV priority; zero for every other type.</summary>
    public int Priority { get; private set; }

    /// <summary>SRV weight; zero for every other type.</summary>
    public int Weight { get; private set; }

    /// <summary>SRV port; zero for every other type.</summary>
    public int Port { get; private set; }

    /// <summary>CAA flags; zero for every other type.</summary>
    public int Flags { get; private set; }

    /// <summary>CAA tag; empty for every other type.</summary>
    public string Tag { get; private set; }

    /// <summary>Creates a record from a draft the token and shape rules accepted. Only <see cref="ZoneTemplate"/> calls it.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="zoneTemplateId">The template the record belongs to.</param>
    /// <param name="position">The record's place in the template.</param>
    /// <param name="draft">The draft.</param>
    internal ZoneTemplateRecord(Guid id, Guid zoneTemplateId, int position, TemplateRecordDraft draft)
    {
        Id = id;
        ZoneTemplateId = zoneTemplateId;
        Position = position;
        Name = draft.Name;
        Type = draft.Type;
        Ttl = draft.Ttl;
        Value = draft.Value;
        Priority = draft.Priority;
        Weight = draft.Weight;
        Port = draft.Port;
        Flags = draft.Flags;
        Tag = draft.Tag;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private ZoneTemplateRecord()
    {
        Name = string.Empty;
        Value = string.Empty;
        Tag = string.Empty;
    }

    /// <summary>Restates the record as a draft, which is the form expansion and the rules read.</summary>
    /// <returns>A draft equal to this record.</returns>
    public TemplateRecordDraft ToDraft()
    {
        return new TemplateRecordDraft(Name, Type, Ttl, Value, Priority, Weight, Port, Flags, Tag);
    }
}
