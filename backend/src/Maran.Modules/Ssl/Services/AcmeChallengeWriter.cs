using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ssl.Common.Interfaces;
using Maran.Modules.Ssl.Resources;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Answers an ACME HTTP-01 challenge by placing the token file inside the site's document root —
/// through the agent, as the owning account.
/// </summary>
/// <remarks>
/// The file lives at <c>&lt;document root&gt;/.well-known/acme-challenge/&lt;token&gt;</c>, which is
/// inside a customer's home directory. That makes it a customer file, and a customer file is written
/// by the agent under that account's uid: the API "spawns nothing at all" and never touches a
/// customer's disk (rules/security.md item 3). Writing it as root would also leave a root-owned file
/// in a directory the customer owns, which they could then neither replace nor delete.
///
/// The agent's nginx template already serves <c>^~ /.well-known/acme-challenge/</c> from the document
/// root, and keeps serving it for a SUSPENDED site — which is why a suspended site can still renew
/// rather than losing TLS as well as its content.
/// </remarks>
public sealed class AcmeChallengeWriter : IAcmeChallengeWriter
{
    /// <summary>
    /// Where a site's document root sits relative to the account's home. The agent lays out
    /// <c>&lt;home&gt;/sites/&lt;domain&gt;</c>, and the paths this type builds are relative to the
    /// home because that is the only thing the agent's file rpcs accept — it canonicalizes and
    /// contains them itself (rules/security.md item 2).
    /// </summary>
    private const string SitesDirectory = "sites";

    /// <summary>The directory an HTTP-01 challenge is served from, fixed by RFC 8555.</summary>
    private const string ChallengeDirectory = ".well-known/acme-challenge";

    /// <summary>
    /// Mode of the written file: readable by everybody, writable only by the owner. The web server
    /// runs as its own user and has to read it, and the file's content is a single-use proof that is
    /// worthless the moment the authorization is consumed.
    /// </summary>
    /// <remarks>
    /// Written in binary and grouped in threes so it reads as the permission bits it is —
    /// <c>rw- r-- r--</c>, which is <c>0644</c>. C# has no octal literal, and <c>0644</c> written as
    /// a decimal literal is the number six hundred and forty-four.
    /// </remarks>
    private const uint ChallengeFileMode = 0b110_100_100;

    /// <summary>The agent, which owns every write inside a customer's home.</summary>
    private readonly IAgentFilesClient _files;

    /// <summary>Creates the writer over the agent's file operations.</summary>
    /// <param name="files">The agent client that writes as the owning account.</param>
    public AcmeChallengeWriter(IAgentFilesClient files)
    {
        _files = files;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> WriteAsync(
        string accountUsername,
        string domain,
        string token,
        string keyAuthorization,
        CancellationToken cancellationToken)
    {
        if (!IsBase64Url(token))
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AcmeChallengeTokenInvalid)));
        }

        var written = await _files.WriteFileAsync(
            accountUsername,
            ChallengePath(domain, token),
            keyAuthorization,
            ChallengeFileMode,
            cancellationToken);

        return written.IsSuccess ? Result<bool>.Ok(true) : Result<bool>.Fail(written.Error!);
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RemoveAsync(
        string accountUsername,
        string domain,
        string token,
        CancellationToken cancellationToken)
    {
        if (!IsBase64Url(token))
        {
            return Result<bool>.Fail(Error.Of(nameof(ErrorMessages.AcmeChallengeTokenInvalid)));
        }

        return await _files.DeleteEntryAsync(
            accountUsername,
            ChallengePath(domain, token),
            recursive: false,
            cancellationToken);
    }

    /// <summary>Builds the challenge file's path relative to the account's home.</summary>
    /// <param name="domain">The domain being validated, which names its document root.</param>
    /// <param name="token">The challenge token, which is also the file name.</param>
    /// <returns>A home-relative path for the agent to canonicalize and contain.</returns>
    /// <remarks>
    /// Neither component is escaped here, and neither needs to be: the domain came from a site row
    /// that a validator accepted, the token came from the authority's own JSON and is base64url by
    /// specification, and the agent re-validates and canonicalizes every path it is given rather than
    /// trusting this caller (rules/architecture.md "Every input is re-validated inside the agent").
    /// </remarks>
    private static string ChallengePath(string domain, string token)
    {
        return $"{SitesDirectory}/{domain}/{ChallengeDirectory}/{token}";
    }

    /// <summary>Whether a token is the base64url text RFC 8555 says a challenge token is.</summary>
    /// <param name="token">The token the authority supplied.</param>
    /// <returns><c>true</c> when the token is non-empty and every character is base64url.</returns>
    /// <remarks>
    /// The token comes from a REMOTE party and is then used as a path component. RFC 8555 §8.3
    /// defines it as base64url, so a conforming authority always passes — but "the specification says
    /// it will be fine" is not a boundary check, and neither was the other half of the defence
    /// written here before: "the agent re-validates and canonicalizes" describes an agent file
    /// service that does not exist yet. A dot or a slash in this position is a traversal out of the
    /// customer's document root (rules/security.md item 2), and the check costs one pass over a
    /// forty-character string.
    /// </remarks>
    private static bool IsBase64Url(string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        foreach (var character in token)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
