using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Domain.Entities;

/// <summary>
/// One DNS zone an account owns: the apex name, the SOA serial and the records. This row — not the zone
/// file on the host — is the truth: the file is rendered from it and a start-up pass rebuilds what is
/// missing (rules/architecture.md "Truth lives in PostgreSQL").
/// </summary>
/// <remarks>
/// <para>
/// <b>This entity is the tenant boundary of every DNS path.</b> The daemon has no notion of a tenant: it
/// answers for whatever zone it was given. Who may see, edit or delete a zone is decided by these rows,
/// through the context's query filter, and a zone another account owns answers 404, never 403.
/// </para>
/// <para>
/// Every change of the zone's content is a method here, and every one of them moves the serial, so a
/// handler cannot change a record and forget to publish a new version. The serial is the optimistic-
/// concurrency token as well: the panel serialises edits of one zone with an advisory lock, and the token
/// is the second guard if two ever overlap.
/// </para>
/// </remarks>
public sealed class Zone
{
    /// <summary>The zone's records, owned by this entity and changed only through its methods.</summary>
    private readonly List<ZoneRecord> _records = [];

    /// <summary>The row's identity, and the only identifier a request may name for a zone.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account that owns the zone. Every tenant-scoped query is closed over this column.</summary>
    public Guid AccountId { get; private set; }

    /// <summary>The apex name, lower case with no trailing dot. Unique across the server.</summary>
    public string Name { get; private set; }

    /// <summary>The SOA serial in <c>YYYYMMDDnn</c> form, increasing with every change.</summary>
    public long Serial { get; private set; }

    /// <summary>The instant the zone was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The instant of the last change to the zone's content.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The zone's records; empty until they are loaded.</summary>
    public IReadOnlyList<ZoneRecord> Records
    {
        get { return _records; }
    }

    /// <summary>Creates the zone with its first serial and no records.</summary>
    /// <param name="id">The row's identity.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="name">
    /// The apex, already passed through <see cref="ZoneNamePolicy.Normalize"/> and accepted by
    /// <see cref="ZoneNamePolicy.Check"/>.
    /// </param>
    /// <param name="now">The creation instant, from the clock.</param>
    public Zone(Guid id, Guid accountId, string name, DateTimeOffset now)
    {
        if (ZoneNamePolicy.Check(name) != ZoneNameVerdict.Accepted)
        {
            throw new ArgumentException("A zone is created from an accepted apex name.", nameof(name));
        }

        Id = id;
        AccountId = accountId;
        Name = name;
        Serial = ZoneSerialPolicy.Next(0, now);
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private Zone()
    {
        Name = string.Empty;
    }

    /// <summary>Adds a customer record to the zone.</summary>
    /// <param name="recordId">The new row's identity.</param>
    /// <param name="draft">The record as stated; it is normalised here and must already be shape-checked.</param>
    /// <param name="maxRecords">The most records the zone may hold.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>
    /// <see cref="RecordRuleVerdict.Accepted"/> when the record was added and the serial moved, else the rule
    /// that refused it and the zone unchanged.
    /// </returns>
    public RecordRuleVerdict AddRecord(Guid recordId, RecordDraft draft, int maxRecords, DateTimeOffset now)
    {
        var normalized = RequireShape(draft);
        if (_records.Count >= maxRecords)
        {
            return RecordRuleVerdict.ZoneFull;
        }

        var verdict = ZoneRecordRules.Check(_records, normalized);
        if (verdict != RecordRuleVerdict.Accepted)
        {
            return verdict;
        }

        _records.Add(new ZoneRecord(recordId, Id, normalized, RecordOrigin.Customer, now));
        Touch(now);

        return RecordRuleVerdict.Accepted;
    }

    /// <summary>Replaces what a customer record says.</summary>
    /// <param name="recordId">The record to change.</param>
    /// <param name="draft">The new content; normalised here and already shape-checked.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>The verdict; the zone is unchanged unless it is <see cref="RecordRuleVerdict.Accepted"/>.</returns>
    public RecordRuleVerdict ChangeRecord(Guid recordId, RecordDraft draft, DateTimeOffset now)
    {
        var normalized = RequireShape(draft);
        var record = _records.SingleOrDefault(existing =>
        {
            return existing.Id == recordId;
        });
        if (record is null)
        {
            return RecordRuleVerdict.RecordNotFound;
        }

        if (record.Origin != RecordOrigin.Customer)
        {
            return RecordRuleVerdict.RecordNotEditable;
        }

        var verdict = ZoneRecordRules.Check(_records.Where(existing =>
        {
            return existing.Id != recordId;
        }).ToList(), normalized);
        if (verdict != RecordRuleVerdict.Accepted)
        {
            return verdict;
        }

        record.Change(normalized);
        Touch(now);

        return RecordRuleVerdict.Accepted;
    }

    /// <summary>Removes a customer record.</summary>
    /// <param name="recordId">The record to remove.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>The verdict; the zone is unchanged unless it is <see cref="RecordRuleVerdict.Accepted"/>.</returns>
    public RecordRuleVerdict RemoveRecord(Guid recordId, DateTimeOffset now)
    {
        var record = _records.SingleOrDefault(existing =>
        {
            return existing.Id == recordId;
        });
        if (record is null)
        {
            return RecordRuleVerdict.RecordNotFound;
        }

        if (record.Origin != RecordOrigin.Customer)
        {
            return RecordRuleVerdict.RecordNotEditable;
        }

        _records.Remove(record);
        Touch(now);

        return RecordRuleVerdict.Accepted;
    }

    /// <summary>
    /// Replaces ALL customer records in one step, as an import does. Records another facility manages are
    /// kept, and every new record is weighed against them and against the ones already accepted.
    /// </summary>
    /// <param name="drafts">The complete new customer record set.</param>
    /// <param name="maxRecords">The most records the zone may hold.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>
    /// <see cref="RecordRuleVerdict.Accepted"/> when the set was swapped in, else the first rule a record
    /// broke, and the zone unchanged.
    /// </returns>
    public RecordRuleVerdict ReplaceRecords(IReadOnlyList<RecordDraft> drafts, int maxRecords, DateTimeOffset now)
    {
        var kept = _records.Where(existing =>
        {
            return existing.Origin != RecordOrigin.Customer;
        }).ToList();
        if (kept.Count + drafts.Count > maxRecords)
        {
            return RecordRuleVerdict.ZoneFull;
        }

        var accepted = new List<ZoneRecord>();
        foreach (var draft in drafts)
        {
            var normalized = RequireShape(draft);
            var verdict = ZoneRecordRules.Check(kept.Concat(accepted).ToList(), normalized);
            if (verdict != RecordRuleVerdict.Accepted)
            {
                return verdict;
            }

            accepted.Add(new ZoneRecord(Guid.NewGuid(), Id, normalized, RecordOrigin.Customer, now));
        }

        _records.RemoveAll(existing =>
        {
            return existing.Origin == RecordOrigin.Customer;
        });
        _records.AddRange(accepted);
        Touch(now);

        return RecordRuleVerdict.Accepted;
    }

    /// <summary>
    /// Moves the serial past <paramref name="servedSerial"/> without changing any record, so the copy of
    /// the zone the daemon holds is replaced by this row's content under a serial that still increases.
    /// </summary>
    /// <param name="servedSerial">The serial the daemon is serving, which may be ahead of this row.</param>
    /// <param name="now">The instant, from the clock.</param>
    public void Reissue(long servedSerial, DateTimeOffset now)
    {
        Serial = Math.Max(Serial, servedSerial);
        Touch(now);
    }

    /// <summary>Normalises a draft and insists it is well-formed: validators ran at the boundary, so a bad one is a bug.</summary>
    /// <param name="draft">The draft as stated.</param>
    /// <returns>The normalised draft.</returns>
    private RecordDraft RequireShape(RecordDraft draft)
    {
        var normalized = RecordShapePolicy.Normalize(draft);
        if (RecordShapePolicy.Check(normalized, Name) != RecordShapeVerdict.Accepted)
        {
            throw new ArgumentException("A zone is changed with a record the shape policy accepted.", nameof(draft));
        }

        return normalized;
    }

    /// <summary>Moves the serial and the change instant.</summary>
    /// <param name="now">The change instant, from the clock.</param>
    private void Touch(DateTimeOffset now)
    {
        Serial = ZoneSerialPolicy.Next(Serial, now);
        UpdatedAt = now;
    }
}
