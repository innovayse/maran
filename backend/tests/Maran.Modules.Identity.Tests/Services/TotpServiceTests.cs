using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using OtpNet;

namespace Maran.Modules.Identity.Tests.Services;
/// <summary>Behavioural contract of totp service.</summary>

public sealed class TotpServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly TotpService _service = new(new FakeClock(Now));

    private static string CodeFor(string secret, TimeSpan offset)
    {
        return new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(Now.Add(offset).UtcDateTime);
    }

    /// <summary>A generated secret is base32 and at least twenty bytes.</summary>
    [Fact]
    public void A_generated_secret_is_base32_and_at_least_twenty_bytes()
    {
        var secret = _service.GenerateSecret();

        Assert.True(Base32Encoding.ToBytes(secret).Length >= 20);
    }

    /// <summary>Two generated secrets differ.</summary>
    [Fact]
    public void Two_generated_secrets_differ()
    {
        Assert.NotEqual(_service.GenerateSecret(), _service.GenerateSecret());
    }

    /// <summary>The provisioning uri names the issuer the user and the secret.</summary>
    [Fact]
    public void The_provisioning_uri_names_the_issuer_the_user_and_the_secret()
    {
        var secret = _service.GenerateSecret();

        var uri = _service.BuildProvisioningUri(secret, "admin");

        Assert.StartsWith("otpauth://totp/", uri, StringComparison.Ordinal);
        Assert.Contains("Maran%3Aadmin", uri, StringComparison.Ordinal);
        Assert.Contains($"secret={secret}", uri, StringComparison.Ordinal);
    }

    /// <summary>The code for the current window verifies.</summary>
    [Fact]
    public void The_code_for_the_current_window_verifies()
    {
        var secret = _service.GenerateSecret();

        Assert.True(_service.Verify(secret, CodeFor(secret, TimeSpan.Zero), null, out _));
    }

    /// <summary>A code from the immediately previous window still verifies.</summary>
    [Fact]
    public void A_code_from_the_immediately_previous_window_still_verifies()
    {
        // One step of tolerance for a phone whose clock lags and a user who types slowly.
        var secret = _service.GenerateSecret();

        Assert.True(_service.Verify(secret, CodeFor(secret, TimeSpan.FromSeconds(-30)), null, out _));
    }

    /// <summary>A code from three windows ago does not verify.</summary>
    [Fact]
    public void A_code_from_three_windows_ago_does_not_verify()
    {
        var secret = _service.GenerateSecret();

        Assert.False(_service.Verify(secret, CodeFor(secret, TimeSpan.FromSeconds(-90)), null, out _));
    }

    /// <summary>A code from the next window does not verify.</summary>
    [Fact]
    public void A_code_from_the_next_window_does_not_verify()
    {
        // Accepting a code the user cannot see yet helps only an attacker with a fast clock.
        var secret = _service.GenerateSecret();

        Assert.False(_service.Verify(secret, CodeFor(secret, TimeSpan.FromSeconds(60)), null, out _));
    }

    /// <summary>A wrong code does not verify.</summary>
    [Fact]
    public void A_wrong_code_does_not_verify()
    {
        Assert.False(_service.Verify(_service.GenerateSecret(), "000000", null, out _));
    }

    /// <summary>A code that was already used does not verify a second time.</summary>
    [Fact]
    public void A_code_that_was_already_used_does_not_verify_a_second_time()
    {
        var secret = _service.GenerateSecret();
        var code = CodeFor(secret, TimeSpan.Zero);
        Assert.True(_service.Verify(secret, code, null, out var window));

        Assert.False(_service.Verify(secret, code, window, out _));
    }
}
