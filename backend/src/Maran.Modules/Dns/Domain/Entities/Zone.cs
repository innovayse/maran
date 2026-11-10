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

    /// <summary>The zone's signing keys: none while the zone is off, exactly one otherwise.</summary>
    private readonly List<ZoneSigningKey> _signingKeys = [];

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

    /// <summary>Where the zone is in its DNSSEC life. Every zone starts <see cref="DnssecState.Off"/>.</summary>
    public DnssecState DnssecState { get; private set; }

    /// <summary>When the current withdrawal from signing began, or null while the zone is not being withdrawn.</summary>
    public DateTimeOffset? DnssecWithdrawalStartedAt { get; private set; }

    /// <summary>The zone's records; empty until they are loaded.</summary>
    public IReadOnlyList<ZoneRecord> Records
    {
        get { return _records; }
    }

    /// <summary>The zone's signing keys; empty until they are loaded, and empty again once the zone is off.</summary>
    public IReadOnlyList<ZoneSigningKey> SigningKeys
    {
        get { return _signingKeys; }
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

    /// <summary>
    /// Adds the records a zone template expanded to, as the zone is created. They are weighed like any other
    /// record, one after another, and arrive with the origin <see cref="RecordOrigin.Template"/>.
    /// </summary>
    /// <param name="drafts">The expanded drafts; each must already be shape-checked against this zone.</param>
    /// <param name="maxRecords">The most records the zone may hold.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>
    /// <see cref="RecordRuleVerdict.Accepted"/> when every draft was added and the serial moved once, else the
    /// first rule a draft broke, and the zone unchanged.
    /// </returns>
    public RecordRuleVerdict AddTemplateRecords(IReadOnlyList<RecordDraft> drafts, int maxRecords, DateTimeOffset now)
    {
        if (CountAgainstLimit(_records) + drafts.Count > maxRecords)
        {
            return RecordRuleVerdict.ZoneFull;
        }

        var accepted = new List<ZoneRecord>();
        foreach (var draft in drafts)
        {
            var normalized = RequireShape(draft);
            var verdict = ZoneRecordRules.Check(_records.Concat(accepted).ToList(), normalized);
            if (verdict != RecordRuleVerdict.Accepted)
            {
                return verdict;
            }

            accepted.Add(new ZoneRecord(Guid.NewGuid(), Id, normalized, RecordOrigin.Template, now));
        }

        if (accepted.Count == 0)
        {
            return RecordRuleVerdict.Accepted;
        }

        _records.AddRange(accepted);
        Touch(now);

        return RecordRuleVerdict.Accepted;
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
        if (CountAgainstLimit(_records) >= maxRecords)
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

    /// <summary>Replaces what a record the owner may edit says; a template record becomes the customer's.</summary>
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

        if (!RecordOriginPolicy.IsOwnerEditable(record.Origin))
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

    /// <summary>Removes a record the owner may remove: the customer's own or one a template created.</summary>
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

        if (!RecordOriginPolicy.IsOwnerEditable(record.Origin))
        {
            return RecordRuleVerdict.RecordNotEditable;
        }

        _records.Remove(record);
        Touch(now);

        return RecordRuleVerdict.Accepted;
    }

    /// <summary>
    /// Adds the TXT record that answers one DNS-01 challenge. It is the only record a zone takes on behalf of
    /// the certificate facility, it does not count against the zone's record cap, and it is bounded instead by
    /// <see cref="ChallengeRecordPolicy.MaxValuesPerName"/> values at one name.
    /// </summary>
    /// <param name="recordId">The new row's identity.</param>
    /// <param name="draft">A TXT at a challenge name, with the digest as its value; normalised here.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>
    /// <see cref="RecordRuleVerdict.Accepted"/> when the record was added and the serial moved, else the rule
    /// that refused it and the zone unchanged.
    /// </returns>
    /// <remarks>
    /// Exempt from the cap on purpose: a zone a customer filled to the limit must not make a certificate
    /// impossible to issue. The caller is handed a draft the challenge policy accepted; anything else is a bug.
    /// </remarks>
    public RecordRuleVerdict AddChallengeRecord(Guid recordId, RecordDraft draft, DateTimeOffset now)
    {
        var normalized = RequireShape(draft);
        if (normalized.Type != RecordType.Txt
            || !ChallengeRecordPolicy.IsChallengeName(normalized.Name)
            || !ChallengeRecordPolicy.IsValue(normalized.Value))
        {
            throw new ArgumentException("A challenge record is a TXT at a challenge name holding a digest.", nameof(draft));
        }

        var atName = _records.Count(existing =>
        {
            return existing.Origin == RecordOrigin.AcmeChallenge && existing.Name == normalized.Name;
        });
        if (atName >= ChallengeRecordPolicy.MaxValuesPerName)
        {
            return RecordRuleVerdict.ChallengeValuesFull;
        }

        var verdict = ZoneRecordRules.Check(_records, normalized);
        if (verdict != RecordRuleVerdict.Accepted)
        {
            return verdict;
        }

        _records.Add(new ZoneRecord(recordId, Id, normalized, RecordOrigin.AcmeChallenge, now));
        Touch(now);

        return RecordRuleVerdict.Accepted;
    }

    /// <summary>Removes a challenge record, and only a challenge record.</summary>
    /// <param name="recordId">The record to remove.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>
    /// <see cref="RecordRuleVerdict.Accepted"/> when it was removed; <see cref="RecordRuleVerdict.RecordNotFound"/>
    /// when the zone holds no such record; <see cref="RecordRuleVerdict.RecordNotEditable"/> when the record is
    /// not a challenge record, which this door never removes.
    /// </returns>
    public RecordRuleVerdict RemoveChallengeRecord(Guid recordId, DateTimeOffset now)
    {
        var record = _records.SingleOrDefault(existing =>
        {
            return existing.Id == recordId;
        });
        if (record is null)
        {
            return RecordRuleVerdict.RecordNotFound;
        }

        if (record.Origin != RecordOrigin.AcmeChallenge)
        {
            return RecordRuleVerdict.RecordNotEditable;
        }

        _records.Remove(record);
        Touch(now);

        return RecordRuleVerdict.Accepted;
    }

    /// <summary>Removes the challenge records created before a cutoff, which no validation is still waiting on.</summary>
    /// <param name="createdBefore">The cutoff; a record created at or after it is kept.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>How many records were removed; the serial moves only when it is more than zero.</returns>
    public int RemoveStaleChallengeRecords(DateTimeOffset createdBefore, DateTimeOffset now)
    {
        var removed = _records.RemoveAll(existing =>
        {
            return existing.Origin == RecordOrigin.AcmeChallenge && existing.CreatedAt < createdBefore;
        });
        if (removed > 0)
        {
            Touch(now);
        }

        return removed;
    }

    /// <summary>
    /// Replaces ALL the records the owner may edit in one step, as an import does, the template's among them.
    /// Records another facility manages are kept, and every new record is weighed against them and against the
    /// ones already accepted.
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
            return !RecordOriginPolicy.IsOwnerEditable(existing.Origin);
        }).ToList();
        if (CountAgainstLimit(kept) + drafts.Count > maxRecords)
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
            return RecordOriginPolicy.IsOwnerEditable(existing.Origin);
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

    /// <summary>Starts signing the zone with a freshly generated key.</summary>
    /// <param name="keyId">The new key row's identity.</param>
    /// <param name="key">The generated key.</param>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>
    /// <see cref="DnssecTransitionVerdict.Accepted"/> when the key was taken and the serial moved, else
    /// <see cref="DnssecTransitionVerdict.NotOff"/> and the zone unchanged.
    /// </returns>
    public DnssecTransitionVerdict EnableSigning(Guid keyId, DnssecKeyMaterial key, DateTimeOffset now)
    {
        if (DnssecState != DnssecState.Off)
        {
            return DnssecTransitionVerdict.NotOff;
        }

        _signingKeys.Add(new ZoneSigningKey(keyId, Id, key, now));
        DnssecState = DnssecState.Signing;
        DnssecWithdrawalStartedAt = null;
        Touch(now);

        return DnssecTransitionVerdict.Accepted;
    }

    /// <summary>
    /// Begins withdrawing the zone from signing: the daemon keeps the same key under its insecure policy while
    /// the owner removes the DS record at the registrar.
    /// </summary>
    /// <param name="now">The change instant, from the clock.</param>
    /// <returns>
    /// <see cref="DnssecTransitionVerdict.Accepted"/> when the withdrawal began and the serial moved, else
    /// <see cref="DnssecTransitionVerdict.NotSigning"/> and the zone unchanged.
    /// </returns>
    public DnssecTransitionVerdict BeginWithdrawal(DateTimeOffset now)
    {
        if (DnssecState != DnssecState.Signing)
        {
            return DnssecTransitionVerdict.NotSigning;
        }

        DnssecState = DnssecState.Withdrawing;
        DnssecWithdrawalStartedAt = now;
        Touch(now);

        return DnssecTransitionVerdict.Accepted;
    }

    /// <summary>
    /// Ends a withdrawal: the zone becomes unsigned and its key is deleted. Refused until the minimum wait since
    /// the withdrawal began has passed, whoever asks.
    /// </summary>
    /// <param name="now">The instant, from the clock; the only time source the wait is measured against.</param>
    /// <returns>
    /// <see cref="DnssecTransitionVerdict.Accepted"/> when the zone is unsigned and the serial moved,
    /// <see cref="DnssecTransitionVerdict.NotWithdrawing"/> or <see cref="DnssecTransitionVerdict.TooEarly"/>
    /// when it is refused, and the zone unchanged.
    /// </returns>
    public DnssecTransitionVerdict FinishWithdrawal(DateTimeOffset now)
    {
        if (DnssecState != DnssecState.Withdrawing)
        {
            return DnssecTransitionVerdict.NotWithdrawing;
        }

        if (!DnssecWithdrawalPolicy.MayFinish(DnssecWithdrawalStartedAt, now))
        {
            return DnssecTransitionVerdict.TooEarly;
        }

        _signingKeys.Clear();
        DnssecState = DnssecState.Off;
        DnssecWithdrawalStartedAt = null;
        Touch(now);

        return DnssecTransitionVerdict.Accepted;
    }

    /// <summary>Counts the records the zone's record limit applies to: every one but a certificate validation record.</summary>
    /// <param name="records">The records to count.</param>
    /// <returns>
    /// How many count against the limit. A challenge record is written by the certificate facility for a few
    /// minutes and is not the customer's to budget, so a zone at its limit can still be validated.
    /// </returns>
    private static int CountAgainstLimit(IEnumerable<ZoneRecord> records)
    {
        return records.Count(record =>
        {
            return record.Origin != RecordOrigin.AcmeChallenge;
        });
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
