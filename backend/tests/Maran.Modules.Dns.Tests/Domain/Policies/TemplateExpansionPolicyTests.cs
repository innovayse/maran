using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>
/// Expanding a template for one zone: variables are substituted, a record whose variable has no value is skipped
/// and reported, and nothing is ever filled in with an invented value.
/// </summary>
public sealed class TemplateExpansionPolicyTests
{
    /// <summary>A context with every variable stated.</summary>
    private static readonly TemplateContext Full = new(
        "shop.example", "203.0.113.5", "2001:db8::5", "ns1.panel.test", "ns2.panel.test", "hostmaster.panel.test", 900);

    /// <summary>The records of an A and an AAAA at the apex.</summary>
    private static readonly IReadOnlyList<TemplateRecordDraft> Addresses =
    [
        new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}"),
        new TemplateRecordDraft("@", RecordType.Aaaa, null, "{ipv6}"),
    ];

    /// <summary>Expands and fails the test when the template is refused.</summary>
    /// <param name="records">The template's records.</param>
    /// <param name="context">The context.</param>
    /// <returns>The expansion.</returns>
    private static TemplateExpansion Expand(IReadOnlyList<TemplateRecordDraft> records, TemplateContext context)
    {
        Assert.True(TemplateExpansionPolicy.TryExpand(records, context, out var expansion));

        return expansion!;
    }

    /// <summary>With no IPv6 address stated the AAAA record is skipped and reported, and nothing is invented.</summary>
    [Fact]
    public void An_empty_ipv6_skips_the_aaaa_record_and_reports_it()
    {
        var expansion = Expand(Addresses, Full with { Ipv6 = string.Empty });

        var created = Assert.Single(expansion.Drafts);
        Assert.Equal(RecordType.A, created.Type);
        var skipped = Assert.Single(expansion.Skipped);
        Assert.Equal(RecordType.Aaaa, skipped.Type);
        Assert.Equal(TemplateVariable.Ipv6, skipped.Variable);
        Assert.Equal("@", skipped.Name);
    }

    /// <summary>Inverse control: with the address stated the AAAA record is created with it and nothing is skipped.</summary>
    [Fact]
    public void A_stated_ipv6_creates_the_aaaa_record()
    {
        var expansion = Expand(Addresses, Full);

        Assert.Equal(["203.0.113.5", "2001:db8::5"], expansion.Drafts.Select(draft =>
        {
            return draft.Value;
        }));
        Assert.Empty(expansion.Skipped);
    }

    /// <summary>A record needing two variables is skipped for the first one that is empty.</summary>
    [Fact]
    public void A_record_is_skipped_for_its_first_missing_variable()
    {
        var records = new[] { new TemplateRecordDraft("@", RecordType.Txt, null, "{ns2} {ipv6}") };

        var expansion = Expand(records, Full with { Ns2 = string.Empty, Ipv6 = string.Empty });

        Assert.Empty(expansion.Drafts);
        Assert.Equal(TemplateVariable.Ns2, Assert.Single(expansion.Skipped).Variable);
    }

    /// <summary>A literal record never needs a variable and is never skipped, whatever the settings hold.</summary>
    [Fact]
    public void A_literal_record_is_never_skipped()
    {
        var records = new[] { new TemplateRecordDraft("@", RecordType.Txt, null, "v=spf1 mx ~all") };
        var empty = new TemplateContext("example.com", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 3600);

        var expansion = Expand(records, empty);

        Assert.Equal("v=spf1 mx ~all", Assert.Single(expansion.Drafts).Value);
        Assert.Empty(expansion.Skipped);
    }

    /// <summary>A record that states no time to live takes the settings' default; one that states it keeps it.</summary>
    [Fact]
    public void The_time_to_live_defaults_to_the_settings_and_an_explicit_one_is_kept()
    {
        var records = new[]
        {
            new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}"),
            new TemplateRecordDraft("www", RecordType.A, 300, "{ipv4}"),
        };

        var expansion = Expand(records, Full);

        Assert.Equal([900, 300], expansion.Drafts.Select(draft =>
        {
            return draft.Ttl;
        }));
    }

    /// <summary>The records come out in the order the template holds them, with every type field carried over.</summary>
    [Fact]
    public void The_order_and_the_type_fields_are_kept()
    {
        var records = new[]
        {
            new TemplateRecordDraft("@", RecordType.Mx, null, "mail.{apex}", Priority: 10),
            new TemplateRecordDraft("@", RecordType.Caa, null, "{apex}", Flags: 128, Tag: "issue"),
        };

        var expansion = Expand(records, Full);

        Assert.Equal("mail.shop.example", expansion.Drafts[0].Value);
        Assert.Equal(10, expansion.Drafts[0].Priority);
        Assert.Equal(128, expansion.Drafts[1].Flags);
        Assert.Equal("issue", expansion.Drafts[1].Tag);
    }

    /// <summary>A template whose record names an unknown variable expands to nothing at all, not to a part of itself.</summary>
    [Fact]
    public void A_refused_token_expands_nothing()
    {
        var records = new[]
        {
            new TemplateRecordDraft("@", RecordType.A, null, "{ipv4}"),
            new TemplateRecordDraft("@", RecordType.Txt, null, "{nope}"),
        };

        var expanded = TemplateExpansionPolicy.TryExpand(records, Full, out var expansion);

        Assert.False(expanded);
        Assert.Null(expansion);
    }

    /// <summary>Normalising gives a name and tag their stored spelling and a missing value an empty one.</summary>
    [Fact]
    public void Normalizing_spells_the_name_and_tag_as_stored()
    {
        var record = new TemplateRecordDraft(" WWW ", RecordType.Caa, null, null!, Tag: " Issue ");

        var normalized = TemplateExpansionPolicy.Normalize(record);

        Assert.Equal("www", normalized.Name);
        Assert.Equal("issue", normalized.Tag);
        Assert.Equal(string.Empty, normalized.Value);
    }
}
