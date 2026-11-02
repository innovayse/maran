using System.Security.Claims;
using Maran.Host.Security;
using Maran.Sdk.Contracts;

namespace Maran.Host.Tests.Security;

/// <summary>Behavioural contract of reading a view-as token's markers (issue #317).</summary>
public sealed class ActorClaimReaderTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    /// <summary>A well-formed actor object is read back with its three fields.</summary>
    [Fact]
    public void A_well_formed_actor_is_read_back()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var json = $"{{\"sub\":\"{userId}\",\"name\":\"root-admin\",\"sid\":\"{sessionId}\"}}";

        Assert.True(ActorClaimReader.TryRead(Principal(new Claim(PanelClaimTypes.Actor, json)), out var actor));
        Assert.Equal(new ActorClaim(userId, "root-admin", sessionId), actor);
    }

    /// <summary>A malformed actor is unreadable but still marks the token as view-as.</summary>
    [Theory]
    [InlineData("not json")]
    [InlineData("\"a string\"")]
    [InlineData("{\"sub\":\"not-a-guid\",\"name\":\"x\",\"sid\":\"also-not\"}")]
    [InlineData("{\"name\":\"x\"}")]
    public void A_malformed_actor_is_unreadable_but_still_marks_view_as(string raw)
    {
        var principal = Principal(new Claim(PanelClaimTypes.Actor, raw));

        Assert.False(ActorClaimReader.TryRead(principal, out _));
        Assert.True(ActorClaimReader.IsViewAs(principal));
    }

    /// <summary>A token carrying only the session-kind marker is still a view-as token.</summary>
    [Fact]
    public void The_session_kind_alone_marks_view_as()
    {
        Assert.True(ActorClaimReader.IsViewAs(Principal(new Claim(PanelClaimTypes.SessionKind, "ViewAs"))));
    }

    /// <summary>An own token carries neither marker.</summary>
    [Fact]
    public void An_own_token_is_not_view_as()
    {
        var principal = Principal(new Claim(PanelClaimTypes.UserId, Guid.NewGuid().ToString()));

        Assert.False(ActorClaimReader.IsViewAs(principal));
        Assert.False(ActorClaimReader.TryRead(principal, out _));
    }
}
