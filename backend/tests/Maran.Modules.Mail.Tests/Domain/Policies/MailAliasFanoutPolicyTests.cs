using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Domain.ValueObjects;

namespace Maran.Modules.Mail.Tests.Domain.Policies;

/// <summary>
/// The whole expansion of an alias or a catch-all counted the way the MTA expands it: nested aliases, a
/// catch-all, an address extension, a cycle and the expansion ceiling.
/// </summary>
public sealed class MailAliasFanoutPolicyTests
{
    /// <summary>The account's own domains in the tests below.</summary>
    private static readonly string[] Domains = ["a.test"];

    /// <summary>Builds an entry on <c>a.test</c>.</summary>
    /// <param name="localPart">The local part, or <c>*</c>.</param>
    /// <param name="targets">The destinations.</param>
    /// <returns>The entry.</returns>
    private static MailAliasEntry Entry(string localPart, params string[] targets)
    {
        return new MailAliasEntry(localPart, "a.test", targets);
    }

    /// <summary>Builds <paramref name="count"/> addresses on a domain the account does not host.</summary>
    /// <param name="count">How many.</param>
    /// <param name="prefix">A prefix that keeps two lists apart.</param>
    /// <returns>The addresses.</returns>
    private static string[] Outside(int count, string prefix)
    {
        return Enumerable.Range(0, count).Select(index =>
        {
            return $"{prefix}{index}@outside.test";
        }).ToArray();
    }

    /// <summary>The single list is counted by its outside targets; an alias that stays inside is present with zero.</summary>
    [Fact]
    public void A_plain_forwarder_is_counted_by_its_outside_targets_and_an_inside_alias_is_left_out()
    {
        var result = MailAliasFanoutPolicy.Resolve(
            Domains,
            [Entry("sales", "x@outside.test", "y@outside.test", "desk@a.test"), Entry("team", "desk@a.test")],
            ["desk@a.test"]);

        Assert.Equal(2, result["sales@a.test"]);
        Assert.Equal(0, result["team@a.test"]);
    }

    /// <summary>An alias of an alias is charged what the inner one delivers (the review defect).</summary>
    [Fact]
    public void An_alias_of_an_alias_inherits_the_outside_deliveries_of_the_inner_one()
    {
        var result = MailAliasFanoutPolicy.Resolve(
            Domains,
            [Entry("outer", "inner@a.test"), Entry("inner", Outside(20, "t"))],
            []);

        Assert.Equal(20, result["outer@a.test"]);
        Assert.Equal(20, result["inner@a.test"]);
    }

    /// <summary>Twenty aliases of twenty outside targets each are 400 destinations, which the MTA stops at its limit.</summary>
    [Fact]
    public void A_tree_of_aliases_is_capped_at_the_expansion_limit()
    {
        var inner = Enumerable.Range(0, 20).Select(index =>
        {
            return Entry($"b{index}", Outside(20, $"b{index}-"));
        }).ToList();
        var outer = Entry("a", inner.Select(entry =>
        {
            return $"{entry.LocalPart}@a.test";
        }).ToArray());

        var result = MailAliasFanoutPolicy.Resolve(Domains, [outer, .. inner], []);

        Assert.Equal(MailAliasFanoutPolicy.ExpansionLimit, result["a@a.test"]);
        Assert.Equal(20, result["b0@a.test"]);
    }

    /// <summary>Two aliases that list each other end where the cycle closes and count what they reach once.</summary>
    [Fact]
    public void A_cycle_terminates_and_counts_each_destination_once()
    {
        var result = MailAliasFanoutPolicy.Resolve(
            Domains,
            [Entry("ping", "pong@a.test", "x@outside.test"), Entry("pong", "ping@a.test", "x@outside.test", "y@outside.test")],
            []);

        Assert.Equal(2, result["ping@a.test"]);
        Assert.Equal(2, result["pong@a.test"]);
    }

    /// <summary>An alias that only points at itself reaches nothing outside and is present with zero.</summary>
    [Fact]
    public void An_alias_that_lists_itself_reaches_nothing()
    {
        var result = MailAliasFanoutPolicy.Resolve(Domains, [Entry("loop", "loop@a.test")], []);

        Assert.Equal(0, result["loop@a.test"]);
        Assert.Single(result);
    }

    /// <summary>A destination named by two lists is delivered once.</summary>
    [Fact]
    public void A_destination_reached_twice_counts_once()
    {
        var result = MailAliasFanoutPolicy.Resolve(
            Domains,
            [Entry("top", "left@a.test", "right@a.test"), Entry("left", "x@outside.test"), Entry("right", "x@outside.test")],
            []);

        Assert.Equal(1, result["top@a.test"]);
    }

    /// <summary>A catch-all is keyed by its domain and counted from its targets.</summary>
    [Fact]
    public void A_catch_all_is_keyed_by_its_domain_and_counted_from_its_targets()
    {
        var result = MailAliasFanoutPolicy.Resolve(Domains, [Entry("*", Outside(5, "c"))], []);

        Assert.Equal(5, result["@a.test"]);
    }

    /// <summary>An alias that lists an address of the account no one claims lands in the catch-all, as the MTA does.</summary>
    [Fact]
    public void An_unclaimed_address_of_the_account_falls_into_the_catch_all()
    {
        var result = MailAliasFanoutPolicy.Resolve(
            Domains,
            [Entry("*", Outside(4, "c")), Entry("front", "nobody@a.test")],
            []);

        Assert.Equal(4, result["front@a.test"]);
    }

    /// <summary>Inverse control: a mailbox ends the expansion, whatever the catch-all says.</summary>
    [Fact]
    public void A_mailbox_ends_the_expansion_even_when_a_catch_all_exists()
    {
        var result = MailAliasFanoutPolicy.Resolve(
            Domains,
            [Entry("*", Outside(4, "c")), Entry("front", "desk@a.test")],
            ["Desk@a.test"]);

        Assert.Equal(0, result["front@a.test"]);
    }

    /// <summary>An alias listing <c>inner+tag@domain</c> reaches the inner alias, as the MTA cuts the extension.</summary>
    [Fact]
    public void An_extension_on_a_listed_alias_does_not_hide_its_expansion()
    {
        var result = MailAliasFanoutPolicy.Resolve(
            Domains,
            [Entry("outer", "inner+news@a.test"), Entry("inner", Outside(3, "t"))],
            []);

        Assert.Equal(3, result["outer@a.test"]);
    }

    /// <summary>Another account's domain is a delivery to someone else and counts one, not its expansion.</summary>
    [Fact]
    public void A_destination_on_another_domain_counts_one()
    {
        var result = MailAliasFanoutPolicy.Resolve(Domains, [Entry("out", "someone@other.test")], []);

        Assert.Equal(1, result["out@a.test"]);
    }

    /// <summary>The recipient key is lower-case and a catch-all is written with a leading at sign.</summary>
    [Fact]
    public void The_recipient_key_is_lower_case_and_a_catch_all_starts_with_an_at_sign()
    {
        Assert.Equal("sales@a.test", MailAliasFanoutPolicy.KeyOf(new MailAliasEntry("Sales", "A.Test", [])));
        Assert.Equal("@a.test", MailAliasFanoutPolicy.KeyOf(new MailAliasEntry("*", "A.Test", [])));
    }

    /// <summary>
    /// An internal alias is a key of its own, so the filter never falls through to the catch-all for it
    /// (the second review's defect: <c>info -> bob</c> was charged the catch-all's twenty).
    /// </summary>
    [Fact]
    public void An_internal_alias_is_present_with_zero_beside_a_catch_all_that_fans_out()
    {
        var result = MailAliasFanoutPolicy.Resolve(
            Domains,
            [Entry("*", Outside(20, "c")), Entry("info", "bob@a.test")],
            ["bob@a.test"]);

        Assert.Equal(0, result["info@a.test"]);
        Assert.Equal(20, result["@a.test"]);
    }

    /// <summary>Inverse control: an address that is neither alias nor mailbox has no key and stays the catch-all's.</summary>
    [Fact]
    public void An_unclaimed_address_has_no_key_of_its_own()
    {
        var result = MailAliasFanoutPolicy.Resolve(Domains, [Entry("*", Outside(2, "c")), Entry("info", "bob@a.test")], []);

        Assert.False(result.ContainsKey("nobody@a.test"));
    }

    /// <summary>A long chain of aliases is walked by an explicit stack, and the far end's target reaches the head.</summary>
    [Fact]
    public void A_long_chain_of_aliases_is_walked_and_its_far_end_reaches_the_head()
    {
        const int depth = 3000;
        var entries = Enumerable.Range(0, depth)
            .Select(index =>
            {
                return Entry($"c{index}", index == depth - 1 ? "end@outside.test" : $"c{index + 1}@a.test");
            })
            .ToList();

        var result = MailAliasFanoutPolicy.Resolve(Domains, entries, []);

        Assert.Equal(1, result["c0@a.test"]);
        Assert.Equal(1, result[$"c{depth - 1}@a.test"]);
    }
}
