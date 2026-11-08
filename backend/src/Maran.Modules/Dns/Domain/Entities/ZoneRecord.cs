using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Entities;

/// <summary>
/// One resource record of a <see cref="Zone"/>. It belongs to a customer through its zone, so the tenant
/// filter reaches it by that required relationship and it carries no account id of its own.
/// </summary>
/// <remarks>
/// Fields are typed columns, one meaning each, and not a free-text rdata blob: a free-text record is how a
/// caller's text becomes a zone-file directive. Only the owning <see cref="Zone"/> creates or changes a
/// record, because its serial must move with every change and the zone's rules weigh every record against
/// the others.
/// </remarks>
public sealed class ZoneRecord
{
    /// <summary>The row's identity, and the only identifier a request may name for a record.</summary>
    public Guid Id { get; private set; }

    /// <summary>The zone the record belongs to.</summary>
    public Guid ZoneId { get; private set; }

    /// <summary>The zone, loaded on demand. The tenant filter of the record is the filter of this zone.</summary>
    public Zone Zone { get; private set; } = null!;

    /// <summary>The owner name relative to the zone, lower case; <c>@</c> is the apex.</summary>
    public string Name { get; private set; }

    /// <summary>The record type.</summary>
    public RecordType Type { get; private set; }

    /// <summary>Time to live, in seconds.</summary>
    public int Ttl { get; private set; }

    /// <summary>The address, target host name, text or CAA value, in the spelling <c>RecordShapePolicy</c> stores.</summary>
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

    /// <summary>Who put the record into the zone, and so who may take it out.</summary>
    public RecordOrigin Origin { get; private set; }

    /// <summary>The instant the record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a record from a normalised, shape-checked draft. Only <see cref="Zone"/> calls it.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="zoneId">The zone the record belongs to.</param>
    /// <param name="draft">The normalised draft.</param>
    /// <param name="origin">Who is writing the record.</param>
    /// <param name="createdAt">The creation instant, from the clock.</param>
    internal ZoneRecord(Guid id, Guid zoneId, RecordDraft draft, RecordOrigin origin, DateTimeOffset createdAt)
    {
        Id = id;
        ZoneId = zoneId;
        Origin = origin;
        CreatedAt = createdAt;
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
    private ZoneRecord()
    {
        Name = string.Empty;
        Value = string.Empty;
        Tag = string.Empty;
    }

    /// <summary>Tells whether this record already holds exactly what <paramref name="draft"/> states, time to live aside.</summary>
    /// <param name="draft">A normalised draft.</param>
    /// <returns>True when name, type, value and every type field are equal.</returns>
    public bool Holds(RecordDraft draft)
    {
        return Name == draft.Name
            && Type == draft.Type
            && Value == draft.Value
            && Priority == draft.Priority
            && Weight == draft.Weight
            && Port == draft.Port
            && Flags == draft.Flags
            && Tag == draft.Tag;
    }

    /// <summary>Restates the record as a draft, which is the form the rules weigh and the wire carries.</summary>
    /// <returns>A draft equal to this record.</returns>
    public RecordDraft ToDraft()
    {
        return new RecordDraft(Name, Type, Ttl, Value, Priority, Weight, Port, Flags, Tag);
    }

    /// <summary>Replaces what the record says with a normalised, shape-checked draft. Only <see cref="Zone"/> calls it.</summary>
    /// <param name="draft">The normalised draft.</param>
    internal void Change(RecordDraft draft)
    {
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
}
