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
}
