using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>
/// What a record may be, type by type, and the injection set a zone file must never see: a newline, a quote,
/// a backslash, a directive, a NUL, an over-long label, a double dot and a slash.
/// </summary>
public sealed class RecordShapePolicyTests
{
    /// <summary>The apex every record is weighed under.</summary>
    private const string Zone = "example.com";

    /// <summary>Weighs a draft after normalising it, as every caller does.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>The verdict.</returns>
    private static RecordShapeVerdict Check(RecordDraft draft)
    {
        return RecordShapePolicy.Check(RecordShapePolicy.Normalize(draft), Zone);
    }

    /// <summary>A well-formed record of every type is accepted.</summary>
    [Theory]
    [InlineData("www", RecordType.A, "203.0.113.7", 0, 0, 0, 0, "")]
    [InlineData("@", RecordType.Aaaa, "2001:db8::1", 0, 0, 0, 0, "")]
    [InlineData("blog", RecordType.Cname, "www.example.com.", 0, 0, 0, 0, "")]
    [InlineData("@", RecordType.Mx, "mail.example.com", 10, 0, 0, 0, "")]
    [InlineData("@", RecordType.Txt, "v=spf1 include:_spf.example.com ~all", 0, 0, 0, 0, "")]
    [InlineData("_sip._tcp", RecordType.Srv, "sip.example.com", 10, 60, 5060, 0, "")]
    [InlineData("@", RecordType.Caa, "letsencrypt.org", 0, 0, 0, 0, "issue")]
    [InlineData("sub", RecordType.Ns, "ns.other.net", 0, 0, 0, 0, "")]
    [InlineData("*", RecordType.A, "203.0.113.7", 0, 0, 0, 0, "")]
    public void A_well_formed_record_of_every_type_is_accepted(
        string name,
        RecordType type,
        string value,
        int priority,
        int weight,
        int port,
        int flags,
        string tag)
    {
        var draft = new RecordDraft(name, type, 300, value, priority, weight, port, flags, tag);

        Assert.Equal(RecordShapeVerdict.Accepted, Check(draft));
    }

    /// <summary>Text that could end a quote, begin an escape or start a line is refused, not escaped.</summary>
    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("say \"hi\"")]
    [InlineData("back\\slash")]
    [InlineData("nul\0byte")]
    [InlineData("tab\there")]
    [InlineData("café")]
    public void Text_outside_the_printable_alphabet_is_refused(string value)
    {
        var draft = new RecordDraft("@", RecordType.Txt, 300, value);

        Assert.Equal(RecordShapeVerdict.ValueInvalid, Check(draft));
    }

    /// <summary>A directive-looking or comment-looking value is plain text inside quotes, so it is accepted as text.</summary>
    /// <remarks>
    /// The protection is that the value is rendered between quotes and cannot contain a quote, so
    /// <c>$INCLUDE</c> and <c>;</c> stay data. This test states that they are accepted, so a later "tightening"
    /// that rejects them is a deliberate decision and not an accident.
    /// </remarks>
    [Fact]
    public void A_directive_looking_text_is_data_inside_the_quotes()
    {
        var draft = new RecordDraft("@", RecordType.Txt, 300, "$INCLUDE /etc/passwd ; comment");

        Assert.Equal(RecordShapeVerdict.Accepted, Check(draft));
    }

    /// <summary>A name with a character outside the label alphabet, or a bad shape, is refused.</summary>
    [Theory]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("a..b")]
    [InlineData(".a")]
    [InlineData("a.")]
    [InlineData("-a")]
    [InlineData("a-")]
    [InlineData("a*")]
    [InlineData("www.*")]
    [InlineData("$INCLUDE")]
    [InlineData("a;b")]
    [InlineData("a\nb")]
    [InlineData("")]
    public void An_owner_name_outside_the_label_law_is_refused(string name)
    {
        var draft = new RecordDraft(name, RecordType.A, 300, "203.0.113.7");

        Assert.Equal(RecordShapeVerdict.NameInvalid, Check(draft));
    }

    /// <summary>A label of 64 characters, and a name that cannot fit under the zone, are refused.</summary>
    [Fact]
    public void An_over_long_label_or_name_is_refused()
    {
        var longLabel = new string('a', 64);
        var longName = string.Join('.', Enumerable.Repeat(new string('a', 60), 4));

        Assert.Equal(
            RecordShapeVerdict.NameInvalid, Check(new RecordDraft(longLabel, RecordType.A, 300, "203.0.113.7")));
        Assert.Equal(
            RecordShapeVerdict.NameInvalid, Check(new RecordDraft(longName, RecordType.A, 300, "203.0.113.7")));
        Assert.Equal(
            RecordShapeVerdict.Accepted,
            Check(new RecordDraft(new string('a', 63), RecordType.A, 300, "203.0.113.7")));
    }

    /// <summary>An address is accepted only in its strict spelling.</summary>
    [Theory]
    [InlineData(RecordType.A, "127.1")]
    [InlineData(RecordType.A, "010.0.0.1")]
    [InlineData(RecordType.A, "0x7f.0.0.1")]
    [InlineData(RecordType.A, "256.0.0.1")]
    [InlineData(RecordType.A, "1.2.\n3.4")]
    [InlineData(RecordType.Aaaa, "fe80::1%eth0")]
    [InlineData(RecordType.Aaaa, "203.0.113.7")]
    [InlineData(RecordType.Aaaa, "2001:db8::g")]
    public void An_address_outside_the_strict_spelling_is_refused(RecordType type, string value)
    {
        var draft = new RecordDraft("@", type, 300, value);

        Assert.Equal(RecordShapeVerdict.ValueInvalid, Check(draft));
    }

    /// <summary>A target must be a host name; an address, a single label and a bad character are refused.</summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("a b.example.com")]
    [InlineData("-bad.example.com")]
    [InlineData("a..example.com")]
    [InlineData("ex_ample.com")]
    [InlineData("")]
    public void A_target_that_is_not_a_host_name_is_refused(string value)
    {
        var draft = new RecordDraft("www", RecordType.Cname, 300, value);

        Assert.Equal(RecordShapeVerdict.ValueInvalid, Check(draft));
    }

    /// <summary>A field a type does not use must stay at its default.</summary>
    [Theory]
    [InlineData(RecordType.A, 1, 0, 0, 0, "", RecordShapeVerdict.PriorityOutOfRange)]
    [InlineData(RecordType.A, 0, 1, 0, 0, "", RecordShapeVerdict.WeightOutOfRange)]
    [InlineData(RecordType.A, 0, 0, 1, 0, "", RecordShapeVerdict.PortOutOfRange)]
    [InlineData(RecordType.A, 0, 0, 0, 1, "", RecordShapeVerdict.FlagsInvalid)]
    [InlineData(RecordType.A, 0, 0, 0, 0, "issue", RecordShapeVerdict.TagInvalid)]
    [InlineData(RecordType.Mx, 0, 1, 0, 0, "", RecordShapeVerdict.WeightOutOfRange)]
    public void A_field_the_type_does_not_use_is_refused(
        RecordType type,
        int priority,
        int weight,
        int port,
        int flags,
        string tag,
        RecordShapeVerdict expected)
    {
        var value = type == RecordType.Mx ? "mail.example.com" : "203.0.113.7";
        var draft = new RecordDraft("@", type, 300, value, priority, weight, port, flags, tag);

        Assert.Equal(expected, Check(draft));
    }

    /// <summary>Numbers outside the unsigned 16-bit range, and CAA flags and tags outside the law, are refused.</summary>
    [Fact]
    public void Numbers_and_tags_outside_the_law_are_refused()
    {
        Assert.Equal(
            RecordShapeVerdict.PriorityOutOfRange,
            Check(new RecordDraft("@", RecordType.Mx, 300, "mail.example.com", Priority: 65536)));
        Assert.Equal(
            RecordShapeVerdict.PriorityOutOfRange,
            Check(new RecordDraft("@", RecordType.Mx, 300, "mail.example.com", Priority: -1)));
        Assert.Equal(
            RecordShapeVerdict.PortOutOfRange,
            Check(new RecordDraft("s", RecordType.Srv, 300, "h.example.com", 1, 1, 70000)));
        Assert.Equal(
            RecordShapeVerdict.FlagsInvalid,
            Check(new RecordDraft("@", RecordType.Caa, 300, "letsencrypt.org", Flags: 1, Tag: "issue")));
        Assert.Equal(
            RecordShapeVerdict.TagInvalid,
            Check(new RecordDraft("@", RecordType.Caa, 300, "letsencrypt.org", Tag: "other")));
        Assert.Equal(
            RecordShapeVerdict.Accepted,
            Check(new RecordDraft("@", RecordType.Caa, 300, "letsencrypt.org", Flags: 128, Tag: "ISSUE")));
    }

    /// <summary>The time to live is held to its bounds at both ends.</summary>
    [Theory]
    [InlineData(59, RecordShapeVerdict.TtlOutOfRange)]
    [InlineData(60, RecordShapeVerdict.Accepted)]
    [InlineData(604800, RecordShapeVerdict.Accepted)]
    [InlineData(604801, RecordShapeVerdict.TtlOutOfRange)]
    public void The_time_to_live_is_held_to_its_bounds(int ttl, RecordShapeVerdict expected)
    {
        Assert.Equal(expected, Check(new RecordDraft("@", RecordType.A, ttl, "203.0.113.7")));
    }

    /// <summary>Normalising gives one spelling to an address, a target and a name.</summary>
    [Fact]
    public void Normalising_gives_one_spelling()
    {
        var draft = RecordShapePolicy.Normalize(
            new RecordDraft(" WWW ", RecordType.Aaaa, 300, "2001:DB8:0:0:0:0:0:1"));
        var target = RecordShapePolicy.Normalize(new RecordDraft("x", RecordType.Cname, 300, "Mail.Example.COM."));

        Assert.Equal("www", draft.Name);
        Assert.Equal("2001:db8::1", draft.Value);
        Assert.Equal("mail.example.com", target.Value);
    }
}
