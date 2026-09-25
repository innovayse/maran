using System.Net;
using System.Security.Claims;
using Maran.Host.RateLimiting;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Http;

namespace Maran.Host.Tests.RateLimiting;

/// <summary>Which identity a rate-limit partition is measured against, and how an address is spelled.</summary>
public sealed class RateLimitPartitionKeyTests
{
    private const string PlainAddress = "203.0.113.7";

    private static DefaultHttpContext Anonymous(IPAddress? peer)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = peer;
        return context;
    }

    /// <summary>An anonymous caller is keyed by address, because it is all that is known about them.</summary>
    [Fact]
    public void An_anonymous_caller_is_keyed_by_address()
    {
        Assert.Equal($"ip:{PlainAddress}", RateLimitPartitionKey.For(Anonymous(IPAddress.Parse(PlainAddress))));
    }

    /// <summary>A dual stack listener's mapped spelling shares one partition with the plain form.</summary>
    /// <remarks>
    /// The consumer-side proof that this call site goes through the panel's shared
    /// <c>ClientAddress</c> rather than <c>RemoteIpAddress.ToString()</c>. Two spellings of one
    /// address would be two partitions, and an anonymous caller would get twice the budget simply by
    /// arriving on the dual-stack socket instead of through nginx.
    /// </remarks>
    [Fact]
    public void A_mapped_ipv4_peer_shares_one_partition_with_its_plain_spelling()
    {
        var mapped = RateLimitPartitionKey.For(Anonymous(IPAddress.Parse($"::ffff:{PlainAddress}")));
        var plain = RateLimitPartitionKey.For(Anonymous(IPAddress.Parse(PlainAddress)));

        Assert.Equal(plain, mapped);
        Assert.Equal($"ip:{PlainAddress}", mapped);
    }

    /// <summary>A connection with no peer is keyed by the marker, never by an address.</summary>
    [Fact]
    public void A_connection_with_no_peer_is_keyed_by_the_marker()
    {
        Assert.Equal("ip:unknown", RateLimitPartitionKey.For(Anonymous(peer: null)));
    }

    /// <summary>An authenticated caller's account outranks their address.</summary>
    [Fact]
    public void An_authenticated_callers_account_outranks_their_address()
    {
        var context = Anonymous(IPAddress.Parse(PlainAddress));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.AccountId, "acct-1")],
            authenticationType: "test"));

        Assert.Equal("account:acct-1", RateLimitPartitionKey.For(context));
    }

    /// <summary>Two different logins on one hosting account share one partition, not two.</summary>
    /// <remarks>
    /// This is the replacement for an HTTP-level test that used to seed two logins on one account
    /// and prove they shared a log-stream budget — impossible now that an account has exactly one
    /// login. What is pinned instead is the mechanism itself: the key is read from
    /// <see cref="PanelClaimTypes.AccountId"/>, so it does not degrade to the per-user claim even
    /// when the two callers are different panel users. A partition key that silently fell back to
    /// <c>UserId</c> would pass every other test in this file and still give each user their own
    /// budget, which is exactly the defect this pins against.
    /// </remarks>
    [Fact]
    public void Two_different_panel_users_on_one_account_share_one_partition()
    {
        var first = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.AccountId, "acct-1"), new Claim(PanelClaimTypes.UserId, "user-1")],
            authenticationType: "test"));
        var second = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.AccountId, "acct-1"), new Claim(PanelClaimTypes.UserId, "user-2")],
            authenticationType: "test"));

        var firstContext = Anonymous(IPAddress.Parse(PlainAddress));
        firstContext.User = first;
        var secondContext = Anonymous(IPAddress.Parse("198.51.100.9"));
        secondContext.User = second;

        var firstKey = RateLimitPartitionKey.For(firstContext);
        var secondKey = RateLimitPartitionKey.For(secondContext);

        Assert.Equal(firstKey, secondKey);
        Assert.Equal("account:acct-1", firstKey);
    }

    /// <summary>With no account claim, the key falls back to the panel user, never to the account.</summary>
    /// <remarks>
    /// The companion to the test above: a panel administrator carries no account claim at all, so
    /// two administrators must NOT collapse onto one partition just because both fall through the
    /// account branch. Pinning the fallback's identity is what would catch a degradation the other
    /// direction — the key silently becoming a constant, or reading the wrong claim, once the
    /// account branch does not fire.
    /// </remarks>
    [Fact]
    public void With_no_account_claim_two_panel_users_get_two_partitions()
    {
        var first = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.UserId, "user-1")],
            authenticationType: "test"));
        var second = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.UserId, "user-2")],
            authenticationType: "test"));

        var firstContext = Anonymous(IPAddress.Parse(PlainAddress));
        firstContext.User = first;
        var secondContext = Anonymous(IPAddress.Parse(PlainAddress));
        secondContext.User = second;

        Assert.NotEqual(RateLimitPartitionKey.For(firstContext), RateLimitPartitionKey.For(secondContext));
    }
}
