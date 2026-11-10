using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;

namespace Maran.Modules.Dns.Tests.Domain.Policies;

/// <summary>
/// Which variables a template record's value may name: the closed catalogue, a well-formed token, and the record
/// types that allow each. Every refusal is paired with an acceptance of the nearest valid value, so a rule that
/// refused everything would fail.
/// </summary>
public sealed class TemplateTokenRulesTests
{
    /// <summary>A token that is not in the catalogue is refused, and a catalogue token in the same place is accepted.</summary>
    [Fact]
    public void An_unknown_token_is_refused_and_a_known_one_is_accepted()
    {
        Assert.Equal(TemplateTokenVerdict.UnknownToken, TemplateTokenRules.Check(RecordType.Txt, "v=x {nope}"));
        Assert.Equal(TemplateTokenVerdict.Accepted, TemplateTokenRules.Check(RecordType.Txt, "v=x {apex}"));
    }

    /// <summary>A name written in another case, with spaces, or empty is not a variable.</summary>
    [Theory]
    [InlineData("{APEX}")]
    [InlineData("{ apex }")]
    [InlineData("{}")]
    [InlineData("{apex2}")]
    public void A_token_must_match_a_variable_name_exactly(string value)
    {
        Assert.Equal(TemplateTokenVerdict.UnknownToken, TemplateTokenRules.Check(RecordType.Txt, value));
    }

    /// <summary>A brace that is not part of one well-formed token is refused, however it is placed.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("{{apex}}")]
    [InlineData("{apex")]
    [InlineData("apex}")]
    [InlineData("a{b{apex}")]
    [InlineData("{apex}}")]
    public void An_unbalanced_brace_is_refused(string value)
    {
        Assert.Equal(TemplateTokenVerdict.Unbalanced, TemplateTokenRules.Check(RecordType.Txt, value));
    }

    /// <summary>A value with no brace at all is a literal and is accepted for every type.</summary>
    [Fact]
    public void A_literal_value_is_accepted_for_every_type()
    {
        foreach (var type in Enum.GetValues<RecordType>())
        {
            Assert.Equal(TemplateTokenVerdict.Accepted, TemplateTokenRules.Check(type, "203.0.113.7"));
        }
    }

    /// <summary>Each variable is accepted exactly where the contract says and refused everywhere else.</summary>
    [Theory]
    [InlineData(RecordType.A, "{ipv4}", true)]
    [InlineData(RecordType.A, "{ipv6}", false)]
    [InlineData(RecordType.A, "{apex}", false)]
    [InlineData(RecordType.Aaaa, "{ipv6}", true)]
    [InlineData(RecordType.Aaaa, "{ipv4}", false)]
    [InlineData(RecordType.Cname, "{apex}.", true)]
    [InlineData(RecordType.Cname, "{ns1}", true)]
    [InlineData(RecordType.Cname, "{ipv4}", false)]
    [InlineData(RecordType.Mx, "mail.{apex}", true)]
    [InlineData(RecordType.Mx, "{hostmaster}", false)]
    [InlineData(RecordType.Srv, "{ns2}", true)]
    [InlineData(RecordType.Ns, "{ns1}", true)]
    [InlineData(RecordType.Txt, "{hostmaster}", true)]
    [InlineData(RecordType.Txt, "{ipv6}", true)]
    [InlineData(RecordType.Caa, "{apex}", true)]
    [InlineData(RecordType.Caa, "{ns1}", false)]
    public void A_variable_is_allowed_only_in_the_record_types_that_take_it(RecordType type, string value, bool allowed)
    {
        var expected = allowed ? TemplateTokenVerdict.Accepted : TemplateTokenVerdict.NotAllowedHere;

        Assert.Equal(expected, TemplateTokenRules.Check(type, value));
    }

    /// <summary>Every record type has a decided answer, so a new type cannot be added without one.</summary>
    [Fact]
    public void Every_record_type_has_a_decided_set_of_allowed_variables()
    {
        foreach (var type in Enum.GetValues<RecordType>())
        {
            Assert.NotNull(TemplateTokenRules.AllowedIn(type));
        }
    }

    /// <summary>The catalogue holds the six variables and spells them in braces.</summary>
    [Fact]
    public void The_catalogue_is_the_six_variables_spelled_in_braces()
    {
        Assert.Equal(
            ["{apex}", "{ipv4}", "{ipv6}", "{ns1}", "{ns2}", "{hostmaster}"],
            TemplateTokenRules.All.Select(TemplateTokenRules.TokenOf));
    }

    /// <summary>Variables are listed once each, in order of appearance.</summary>
    [Fact]
    public void The_variables_in_a_value_are_listed_once_in_order()
    {
        var found = TemplateTokenRules.VariablesIn("{ns1} {apex} {ns1}");

        Assert.Equal([TemplateVariable.Ns1, TemplateVariable.Apex], found);
        Assert.Empty(TemplateTokenRules.VariablesIn("literal"));
    }

    /// <summary>Reading the variables of a value the rules refused is a bug and says so.</summary>
    [Fact]
    public void Reading_the_variables_of_a_refused_value_throws()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            return TemplateTokenRules.VariablesIn("{nope}");
        });
    }

    /// <summary>Substitution replaces every token and leaves the rest of the value as written.</summary>
    [Fact]
    public void Substitution_replaces_each_token_and_keeps_the_rest()
    {
        var value = TemplateTokenRules.Substitute("mail.{apex} via {ns1}", TemplateContext.Sample);

        Assert.Equal("mail.example.com via ns1.example.com", value);
    }

    /// <summary>What was substituted is never read for tokens again, so a value cannot expand into a second round.</summary>
    [Fact]
    public void Substituted_text_is_not_expanded_again()
    {
        var context = TemplateContext.Sample with { Apex = "{ns1}" };

        var value = TemplateTokenRules.Substitute("{apex}", context);

        Assert.Equal("{ns1}", value);
    }
}
