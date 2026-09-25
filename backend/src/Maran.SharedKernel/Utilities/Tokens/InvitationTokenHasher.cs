using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Maran.SharedKernel.Utilities.Tokens;

/// <summary>
/// Generates invitation tokens and reduces them to the digest stored beside an outstanding
/// invitation.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors <see cref="PasswordResetTokenHasher"/> byte for byte: the same 32-byte CSPRNG draw,
/// base64url-encoded for the link, reduced to a base64-encoded SHA-256 digest for storage. SHA-256 is
/// correct here for the identical reason it is correct there — the input is a full-entropy secret
/// with no guessable structure, so a deliberately slow hash would cost latency on a public endpoint
/// without buying any resistance.
/// </para>
/// <para>
/// <b>It is a separate type from <see cref="PasswordResetTokenHasher"/> rather than a shared one.</b>
/// The two hash different secrets into different tables with different lifetimes and different
/// consumers (an invited user setting a first password, versus an existing user replacing one they
/// forgot). A single helper would mean a token from one table hashes identically to a token from the
/// other, so a digest collision — or a future change to one that a reviewer assumed also covered the
/// other — would make the tables interchangeable wherever either is queried by digest alone.
/// </para>
/// <para>
/// It lives in <c>Utilities/Tokens/</c> for the same reason its neighbours do (see
/// <see cref="PasswordResetTokenHasher"/>'s remarks): this is a keyless, saltless, unparameterised
/// digest, nothing here is configurable, and nothing here holds a secret at rest.
/// </para>
/// </remarks>
public static class InvitationTokenHasher
{
    /// <summary>Length of a generated token, in bytes. Thirty-two, matching every other token in this module.</summary>
    private const int TokenBytes = 32;

    /// <summary>Generates a new invitation token.</summary>
    /// <returns>A base64url-encoded token, safe to put in a link.</returns>
    public static string Generate()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
    }

    /// <summary>Reduces a token to the digest stored in the database.</summary>
    /// <param name="token">The plaintext invitation token as the caller presented it.</param>
    /// <returns>The base64-encoded SHA-256 digest.</returns>
    public static string Hash(string token)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
