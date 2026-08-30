using Maran.Modules.Identity.Common.Interfaces;
using OtpNet;

namespace Maran.Modules.Identity.Services;

/// <summary>RFC 6238 TOTP over a 30-second step, with replay protection.</summary>
/// <remarks>
/// The current instant comes from <see cref="IClock"/> and is passed to the algorithm explicitly.
/// Otp.NET's parameterless overloads read the ambient clock, which would make this service's
/// behaviour depend on wall time — untestable without waiting, and a banned API besides
/// (rules/csharp.md "Forbidden").
/// </remarks>
public sealed class TotpService : ITotpService
{
    /// <summary>Length of a generated shared secret, in bytes (160 bits, as RFC 4226 recommends).</summary>
    private const int SecretBytes = 20;

    /// <summary>The issuer shown beside the account in an authenticator app.</summary>
    private const string Issuer = "Maran";

    /// <summary>The panel's clock, deciding which time step "now" is.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="clock">The panel's clock.</param>
    public TotpService(IClock clock)
    {
        _clock = clock;
    }

    /// <inheritdoc />
    public string GenerateSecret()
    {
        return Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(SecretBytes));
    }

    /// <inheritdoc />
    public string BuildProvisioningUri(string secret, string username)
    {
        var label = Uri.EscapeDataString($"{Issuer}:{username}");
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";
    }

    /// <inheritdoc />
    public bool Verify(string secret, string code, long? lastAcceptedWindow, out long window)
    {
        window = 0;

        var totp = new Totp(Base32Encoding.ToBytes(secret));

        // One step back and none forward. The step back tolerates a phone whose clock lags and a
        // user who types slowly; a step FORWARD would accept a code the user cannot yet see, which
        // helps nobody but an attacker with a fast clock.
        if (!totp.VerifyTotp(
                _clock.UtcNow.UtcDateTime,
                code,
                out var matched,
                new VerificationWindow(previous: 1, future: 0)))
        {
            return false;
        }

        // A code stays valid for its whole window, so without this a code seen once — over a
        // shoulder, in a keylogger, in a proxied request — could be replayed for the rest of it.
        if (lastAcceptedWindow is { } used && matched <= used)
        {
            return false;
        }

        window = matched;
        return true;
    }
}
