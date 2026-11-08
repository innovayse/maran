using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Dns.Domain.Policies;

/// <summary>
/// Decides whether one record is well-formed for its type, and gives it the one spelling it is stored in.
/// The boundary validators and the zone both call it, so the rule is written once and the agent's own
/// re-validation is the second, independent reader of the same law.
/// </summary>
/// <remarks>
/// Every field a type does not use must stay at its default. That is what gives each field one meaning:
/// a nonzero weight on an A record is refused, not ignored.
/// </remarks>
public static class RecordShapePolicy
{
    /// <summary>Gives a draft its stored spelling: lower-case name and tag, canonical addresses, targets without a trailing dot.</summary>
    /// <param name="draft">The draft as stated.</param>
    /// <returns>
    /// The normalised draft. Normalising decides nothing, and a value that is not an address or a host name is
    /// returned trimmed and otherwise untouched, for <see cref="Check"/> to refuse.
    /// </returns>
    public static RecordDraft Normalize(RecordDraft draft)
    {
        var value = (draft.Value ?? string.Empty).Trim();
        value = draft.Type switch
        {
            RecordType.A => value,
            RecordType.Aaaa => IpAddressRule.CanonicalIpv6(value),
            RecordType.Cname or RecordType.Mx or RecordType.Srv or RecordType.Ns => NormalizeTarget(value),
            _ => draft.Value ?? string.Empty,
        };

        return draft with
        {
            Name = RecordNamePolicy.Normalize(draft.Name),
            Value = value,
            Tag = (draft.Tag ?? string.Empty).Trim().ToLowerInvariant(),
        };
    }

    /// <summary>Weighs a normalised draft against the law of its type.</summary>
    /// <param name="draft">A draft already passed through <see cref="Normalize"/>.</param>
    /// <param name="zoneName">The zone's apex, which the owner name must fit under.</param>
    /// <returns><see cref="RecordShapeVerdict.Accepted"/>, or the first field found wrong.</returns>
    public static RecordShapeVerdict Check(RecordDraft draft, string zoneName)
    {
        if (!RecordNamePolicy.IsValid(draft.Name, zoneName))
        {
            return RecordShapeVerdict.NameInvalid;
        }

        if (draft.Ttl is < DnsLimits.MinTtl or > DnsLimits.MaxTtl)
        {
            return RecordShapeVerdict.TtlOutOfRange;
        }

        return draft.Type == RecordType.Caa ? CheckCaa(draft) : CheckFields(ValueIsValid(draft), draft);
    }

    /// <summary>Tells whether the value is acceptable for a type other than CAA.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>True when the value is an address, a host name or text as the type requires.</returns>
    private static bool ValueIsValid(RecordDraft draft)
    {
        return draft.Type switch
        {
            RecordType.A => IpAddressRule.IsIpv4(draft.Value),
            RecordType.Aaaa => IpAddressRule.IsIpv6(draft.Value),
            RecordType.Txt => RecordTextRule.IsAcceptable(draft.Value),
            RecordType.Cname or RecordType.Mx or RecordType.Srv or RecordType.Ns => IsTarget(draft.Value),
            _ => false,
        };
    }

    /// <summary>Checks the value verdict and the numeric fields of every type except CAA.</summary>
    /// <param name="valueIsValid">Whether the type's value rule accepted the value.</param>
    /// <param name="draft">The draft.</param>
    /// <returns>The verdict.</returns>
    private static RecordShapeVerdict CheckFields(bool valueIsValid, RecordDraft draft)
    {
        if (!valueIsValid)
        {
            return RecordShapeVerdict.ValueInvalid;
        }

        if (!IsInRange(draft.Priority, RecordTypeFields.UsesPriority(draft.Type)))
        {
            return RecordShapeVerdict.PriorityOutOfRange;
        }

        var usesWeightAndPort = RecordTypeFields.UsesWeightAndPort(draft.Type);
        if (!IsInRange(draft.Weight, usesWeightAndPort))
        {
            return RecordShapeVerdict.WeightOutOfRange;
        }

        if (!IsInRange(draft.Port, usesWeightAndPort))
        {
            return RecordShapeVerdict.PortOutOfRange;
        }

        if (draft.Flags != 0)
        {
            return RecordShapeVerdict.FlagsInvalid;
        }

        return draft.Tag.Length == 0 ? RecordShapeVerdict.Accepted : RecordShapeVerdict.TagInvalid;
    }

    /// <summary>Checks a CAA record: flags 0 or 128, a known tag, text value, and no priority, weight or port.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>The verdict.</returns>
    private static RecordShapeVerdict CheckCaa(RecordDraft draft)
    {
        if (!RecordTextRule.IsAcceptable(draft.Value))
        {
            return RecordShapeVerdict.ValueInvalid;
        }

        if (draft.Priority != 0)
        {
            return RecordShapeVerdict.PriorityOutOfRange;
        }

        if (draft.Weight != 0)
        {
            return RecordShapeVerdict.WeightOutOfRange;
        }

        if (draft.Port != 0)
        {
            return RecordShapeVerdict.PortOutOfRange;
        }

        if (draft.Flags is not (0 or DnsLimits.CaaCriticalFlag))
        {
            return RecordShapeVerdict.FlagsInvalid;
        }

        return DnsLimits.CaaTags.Contains(draft.Tag) ? RecordShapeVerdict.Accepted : RecordShapeVerdict.TagInvalid;
    }

    /// <summary>Tells whether a numeric field is acceptable: zero when unused, an unsigned 16-bit number when used.</summary>
    /// <param name="number">The field.</param>
    /// <param name="used">Whether the record's type uses the field.</param>
    /// <returns>True when acceptable.</returns>
    private static bool IsInRange(int number, bool used)
    {
        return used ? number is >= 0 and <= DnsLimits.MaxUnsigned16 : number == 0;
    }

    /// <summary>Tells whether text is a host name usable as a record target.</summary>
    /// <param name="value">The text, already without a trailing dot.</param>
    /// <returns>True for a valid host name of at least two labels within the length bound.</returns>
    private static bool IsTarget(string value)
    {
        return value.Length <= DnsLimits.MaxNameLength && HostNameRule.IsHostName(value);
    }

    /// <summary>Spells a target host name as stored: lower case, without a trailing dot.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The stored spelling.</returns>
    private static string NormalizeTarget(string value)
    {
        var trimmed = value.EndsWith('.') ? value[..^1] : value;

        return trimmed.ToLowerInvariant();
    }
}
