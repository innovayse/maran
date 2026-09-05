using Maran.Modules.Identity.Options;

namespace Maran.Modules.Identity.Tests.Options;
/// <summary>Behavioural contract of jwt options.</summary>

public sealed class JwtOptionsTests
{
    /// <summary>A signing key shorter than thirty two bytes is rejected.</summary>
    [Fact]
    public void A_signing_key_shorter_than_thirty_two_bytes_is_rejected()
    {
        var options = new JwtOptions { SigningKey = Convert.ToBase64String(new byte[31]) };

        Assert.False(options.HasValidSigningKey());
    }

    /// <summary>A thirty two byte signing key is accepted.</summary>
    [Fact]
    public void A_thirty_two_byte_signing_key_is_accepted()
    {
        var options = new JwtOptions { SigningKey = Convert.ToBase64String(new byte[32]) };

        Assert.True(options.HasValidSigningKey());
    }

    /// <summary>A signing key that is not base64 is rejected rather than throwing.</summary>
    [Fact]
    public void A_signing_key_that_is_not_base64_is_rejected_rather_than_throwing()
    {
        var options = new JwtOptions { SigningKey = "not base64 at all" };

        Assert.False(options.HasValidSigningKey());
    }

    /// <summary>A missing signing key is rejected.</summary>
    [Fact]
    public void A_missing_signing_key_is_rejected()
    {
        Assert.False(new JwtOptions().HasValidSigningKey());
    }

    /// <summary>The access token lifetime defaults to the fifteen minutes the spec asks for.</summary>
    [Fact]
    public void The_access_token_lifetime_defaults_to_the_fifteen_minutes_the_spec_asks_for()
    {
        Assert.Equal(15, new JwtOptions().AccessTokenMinutes);
    }
}
