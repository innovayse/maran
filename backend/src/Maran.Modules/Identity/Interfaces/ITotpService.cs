namespace Maran.Modules.Identity.Interfaces;

/// <summary>Generates and verifies the time-based codes of the panel's second factor (spec §10).</summary>
public interface ITotpService
{
    /// <summary>Generates a fresh shared secret for an enrolment.</summary>
    /// <returns>The base32-encoded secret, to be shown once and then stored encrypted.</returns>
    string GenerateSecret();

    /// <summary>Builds the <c>otpauth://</c> URI an authenticator app scans.</summary>
    /// <param name="secret">The base32-encoded shared secret.</param>
    /// <param name="username">The account label shown in the app.</param>
    /// <returns>The provisioning URI.</returns>
    string BuildProvisioningUri(string secret, string username);

    /// <summary>Verifies a code against a secret.</summary>
    /// <param name="secret">The base32-encoded shared secret.</param>
    /// <param name="code">The six digits the user typed.</param>
    /// <param name="lastAcceptedWindow">The time step of the last accepted code, or null.</param>
    /// <param name="window">Receives the time step the code belongs to, when it verified.</param>
    /// <returns>True when the code is valid and has not already been used.</returns>
    bool Verify(string secret, string code, long? lastAcceptedWindow, out long window);
}
