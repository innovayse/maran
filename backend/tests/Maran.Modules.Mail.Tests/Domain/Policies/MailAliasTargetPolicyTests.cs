using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Domain.Policies;

/// <summary>
/// The one place the destinations of an alias are normalised and judged: the grammar it shares with the
/// agent, the count ceiling, repeats, which targets leave the account, and the kind they make.
/// </summary>
/// <remarks>
/// The accepted and refused cases are DATA on purpose. Each row is a case the agent's own grammar
/// (<c>agent-core/src/validation/mail/mail_address.rs</c> and <c>mail_alias_targets.rs</c>) answers the same
/// way, written beside the reason, so a reader changing either side can hold the other to the same table:
/// every accepted row's normalised form parses in the agent, and every refused row is refused there for
/// the reason named.
/// </remarks>
public sealed class MailAliasTargetPolicyTests
{
    /// <summary>The names of the account's own mail domains in the tests below.</summary>
    private static readonly string[] OwnDomains = ["example.com", "example.net"];

    /// <summary>Typed forms and the one spelling stored; the agent parses each stored form and lower-cases it the same way.</summary>
    public static TheoryData<string, string> AcceptedTargets
    {
        get
        {
            return new TheoryData<string, string>
            {
                { "bob@example.com", "bob@example.com" },
                { "Alice@Example.COM", "alice@example.com" },
                { "  padded@example.com  ", "padded@example.com" },
                { "name+tag@example.org", "name+tag@example.org" },
                { "first.last@sub.example.co.uk", "first.last@sub.example.co.uk" },
                { "a_b-c@example.com", "a_b-c@example.com" },
                { "a@my-host.example.com", "a@my-host.example.com" },
                { "x@e.co", "x@e.co" },
                { new string('a', 64) + "@example.com", new string('a', 64) + "@example.com" },
            };
        }
    }

    /// <summary>Typed forms the agent refuses, each for the reason named in the case.</summary>
    public static TheoryData<string> RefusedTargets
    {
        get
        {
            return new TheoryData<string>
            {
                { "alice" },
                { string.Empty },
                { "   " },
                { "a@b@example.com" },
                { "a@@example.com" },
                { "\"a b\"@example.com" },
                { "a b@example.com" },
                { "user@[127.0.0.1]" },
                { "user@localhost" },
                { "user@host" },
                { "a@x.localhost" },
                { "a@printer.local" },
                { "a@x.localdomain" },
                { "a@x.internal" },
                { "a@x.lan" },
                { "a@x.home.arpa" },
                { "a@x.invalid" },
                { "a@1.2.3.4" },
                { "a@exa_mple.com" },
                { "a@-bad.example.com" },
                { "a@bad-.example.com" },
                { "a@example..com" },
                { "a@.example.com" },
                { "a@example.com." },
                { ".a@example.com" },
                { "a.@example.com" },
                { "a..b@example.com" },
                { "+a@example.com" },
                { "a@example.com\nb@example.com" },
                { "a\r@example.com" },
                { new string('a', 65) + "@example.com" },
                { "a@" + new string('b', 64) + ".example.com" },
                { "a@" + string.Join('.', new string('b', 63), new string('c', 63), new string('d', 63), "com") },
                { "@example.com" },
                { "a@" },
            };
        }
    }

    /// <summary>A typed list is stored in one spelling and in the order given.</summary>
    /// <param name="typed">What the caller typed.</param>
    /// <param name="stored">What is stored.</param>
    [Theory]
    [MemberData(nameof(AcceptedTargets))]
    public void A_target_the_agent_accepts_is_stored_trimmed_and_lower_cased(string typed, string stored)
    {
        var result = MailAliasTargetPolicy.Normalise([typed]);

        Assert.True(result.IsSuccess);
        Assert.Equal(stored, Assert.Single(result.Value));
    }

    /// <summary>A target the agent refuses is refused here with the typed invalid-target code.</summary>
    /// <param name="typed">What the caller typed.</param>
    [Theory]
    [MemberData(nameof(RefusedTargets))]
    public void A_target_the_agent_refuses_is_refused_as_invalid(string typed)
    {
        var result = MailAliasTargetPolicy.Normalise(["ok@example.com", typed]);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>The order the caller gave is the order stored.</summary>
    [Fact]
    public void Targets_keep_the_order_given()
    {
        var result = MailAliasTargetPolicy.Normalise(["b@example.com", "a@example.org", "c@example.net"]);

        Assert.Equal(["b@example.com", "a@example.org", "c@example.net"], result.Value);
    }

    /// <summary>An empty or missing list is refused: an address with nowhere to go delivers nothing.</summary>
    [Fact]
    public void An_empty_or_missing_list_is_refused_as_invalid()
    {
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, MailAliasTargetPolicy.Normalise([]).Error!.Code);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, MailAliasTargetPolicy.Normalise(null).Error!.Code);
    }

    /// <summary>The ceiling is accepted and one more is refused with the limit code, before any address is read.</summary>
    [Fact]
    public void The_ceiling_is_accepted_and_one_more_is_refused_before_any_address_is_read()
    {
        var atCeiling = Enumerable.Range(0, MailDefaults.MaxAliasTargets)
            .Select(index =>
            {
                return $"u{index}@example.com";
            })
            .ToList();
        Assert.True(MailAliasTargetPolicy.Normalise(atCeiling).IsSuccess);

        // The extra entry is not even an address: the count is judged first, as the agent does.
        var beyond = atCeiling.Append("not an address").ToList();
        var result = MailAliasTargetPolicy.Normalise(beyond);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetLimit, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>A repeat is refused even when spelled in another case, because it would deliver twice.</summary>
    [Fact]
    public void A_repeat_is_refused_even_when_spelled_in_another_case()
    {
        var result = MailAliasTargetPolicy.Normalise(["a@example.com", "b@example.com", "A@Example.COM"]);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailAliasErrorCodes.MailAliasTargetInvalid, result.Error!.Code);
    }

    /// <summary>Two different addresses that share a local part or a domain are not repeats.</summary>
    [Fact]
    public void Addresses_that_share_only_a_half_are_not_repeats()
    {
        var result = MailAliasTargetPolicy.Normalise(["a@example.com", "a@example.org", "b@example.com"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
    }

    /// <summary>A target on one of the account's own domains stays inside, whatever its case.</summary>
    [Fact]
    public void A_target_on_an_own_domain_is_internal_and_any_other_is_external()
    {
        Assert.False(MailAliasTargetPolicy.IsExternal("bob@example.com", OwnDomains));
        Assert.False(MailAliasTargetPolicy.IsExternal("bob@Example.NET", OwnDomains));
        Assert.True(MailAliasTargetPolicy.IsExternal("bob@example.org", OwnDomains));
    }

    /// <summary>A subdomain of an own domain is not the own domain: only a domain the account hosts is internal.</summary>
    [Fact]
    public void A_subdomain_of_an_own_domain_is_external()
    {
        Assert.True(MailAliasTargetPolicy.IsExternal("bob@mail.example.com", OwnDomains));
        Assert.True(MailAliasTargetPolicy.IsExternal("bob@notexample.com", OwnDomains));
    }

    /// <summary>With no domains of its own every target is external.</summary>
    [Fact]
    public void An_account_with_no_domains_has_only_external_targets()
    {
        Assert.True(MailAliasTargetPolicy.IsExternal("bob@example.com", []));
    }

    /// <summary>The kind follows where the targets go: inside is an alias, any way out is a forwarder.</summary>
    [Fact]
    public void The_kind_is_an_alias_inside_the_account_and_a_forwarder_when_any_target_leaves_it()
    {
        Assert.Equal(
            MailAliasKind.Alias,
            MailAliasTargetPolicy.KindFor(["a@example.com", "b@example.net"], OwnDomains));
        Assert.Equal(
            MailAliasKind.Forwarder,
            MailAliasTargetPolicy.KindFor(["a@example.com", "b@example.org"], OwnDomains));
        Assert.Equal(
            MailAliasKind.Forwarder,
            MailAliasTargetPolicy.KindFor(["b@example.org"], OwnDomains));
    }

    /// <summary>An external target is refused while the host's switch is off, and accepted when it is on.</summary>
    [Fact]
    public void An_external_target_needs_the_hosts_switch()
    {
        var targets = new[] { "me@example.org" };

        var refused = MailAliasTargetPolicy.Judge(
            MailAliasKind.Forwarder, targets, OwnDomains, allowExternalForwarding: false);
        var accepted = MailAliasTargetPolicy.Judge(
            MailAliasKind.Forwarder, targets, OwnDomains, allowExternalForwarding: true);

        Assert.Equal(MailAliasErrorCodes.MailAliasExternalTargetNotAllowed, refused!.Code);
        Assert.Equal(ErrorType.Validation, refused.Type);
        Assert.Null(accepted);
    }

    /// <summary>Internal targets never need the switch.</summary>
    [Fact]
    public void An_internal_target_is_accepted_with_the_switch_off()
    {
        Assert.Null(MailAliasTargetPolicy.Judge(
            MailAliasKind.Alias, ["bob@example.com"], OwnDomains, allowExternalForwarding: false));
    }

    /// <summary>A stated alias or forwarder that the targets contradict is refused in both directions.</summary>
    [Fact]
    public void A_stated_kind_the_targets_contradict_is_refused()
    {
        var aliasButOutside = MailAliasTargetPolicy.Judge(
            MailAliasKind.Alias, ["me@example.org"], OwnDomains, allowExternalForwarding: true);
        var forwarderButInside = MailAliasTargetPolicy.Judge(
            MailAliasKind.Forwarder, ["bob@example.com"], OwnDomains, allowExternalForwarding: true);

        Assert.Equal(MailAliasErrorCodes.MailAliasKindMismatch, aliasButOutside!.Code);
        Assert.Equal(MailAliasErrorCodes.MailAliasKindMismatch, forwarderButInside!.Code);
    }

    /// <summary>The external refusal wins over the mismatch, because it does not depend on how the request was described.</summary>
    [Fact]
    public void The_external_refusal_comes_before_the_mismatch()
    {
        var error = MailAliasTargetPolicy.Judge(
            MailAliasKind.Alias, ["me@example.org"], OwnDomains, allowExternalForwarding: false);

        Assert.Equal(MailAliasErrorCodes.MailAliasExternalTargetNotAllowed, error!.Code);
    }

    /// <summary>An update states no kind and a catch-all is never contradicted by its targets.</summary>
    [Fact]
    public void A_missing_kind_and_a_catch_all_are_never_a_mismatch()
    {
        Assert.Null(MailAliasTargetPolicy.Judge(
            requestedKind: null, ["me@example.org"], OwnDomains, allowExternalForwarding: true));
        Assert.Null(MailAliasTargetPolicy.Judge(
            MailAliasKind.CatchAll, ["bob@example.com"], OwnDomains, allowExternalForwarding: false));
        Assert.Null(MailAliasTargetPolicy.Judge(
            MailAliasKind.CatchAll, ["me@example.org"], OwnDomains, allowExternalForwarding: true));
    }

    /// <summary>A catch-all that leaves the account still needs the switch.</summary>
    [Fact]
    public void A_catch_all_to_an_outside_address_still_needs_the_switch()
    {
        var error = MailAliasTargetPolicy.Judge(
            MailAliasKind.CatchAll, ["me@example.org"], OwnDomains, allowExternalForwarding: false);

        Assert.Equal(MailAliasErrorCodes.MailAliasExternalTargetNotAllowed, error!.Code);
    }
}
