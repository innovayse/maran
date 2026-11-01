using System.Diagnostics;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ssl.Interfaces;
using Maran.Modules.Ssl.Models;
using Maran.Modules.Ssl.Resources;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Answers an ACME HTTP-01 challenge by placing the token file inside the site's document root —
/// through the agent, as the owning account — or, for the panel's own host name, in the agent's
/// panel challenge directory (issue #132).
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
///
/// The panel's own challenge belongs to no account, so it does not go through the file rpcs at all:
/// the agent's <c>WritePanelChallenge</c> places it in a root-owned directory the panel's port-80
/// block serves. Both paths meet the agent's free-space floor before anything is written (#148), and
/// a refusal there comes back as the floor's own code.
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

    /// <summary>The agent's TLS operations, which own the panel's challenge directory.</summary>
    private readonly IAgentSslClient _ssl;

    /// <summary>Creates the writer over the agent's file and TLS operations.</summary>
    /// <param name="files">The agent client that writes as the owning account.</param>
    /// <param name="ssl">The agent client that writes the panel's own challenge.</param>
    public AcmeChallengeWriter(IAgentFilesClient files, IAgentSslClient ssl)
    {
        _files = files;
        _ssl = ssl;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> WriteAsync(
        AcmeChallengeTarget target,
        string domain,
        string token,
        string keyAuthorization,
        CancellationToken cancellationToken)
    {
        if (!IsBase64Url(token))
        {
            return Result<bool>.Fail(
                Error.Of(nameof(ErrorMessages.AcmeChallengeTokenInvalid), ErrorType.UpstreamRefused));
        }

        return target switch
        {
            SiteDocumentRootChallenge site => await WriteIntoDocumentRootAsync(
                site, domain, token, keyAuthorization, cancellationToken),
            PanelChallengeDirectory => ChallengeFailure(
                await _ssl.WritePanelChallengeAsync(token, keyAuthorization, cancellationToken),
                nameof(ErrorMessages.PanelChallengeWriteFailed)),
            _ => throw new UnreachableException("AcmeChallengeTarget is closed to the two targets this module declares."),
        };
    }

    /// <inheritdoc />
    public async Task<Result<bool>> RemoveAsync(
        AcmeChallengeTarget target,
        string domain,
        string token,
        CancellationToken cancellationToken)
    {
        if (!IsBase64Url(token))
        {
            return Result<bool>.Fail(
                Error.Of(nameof(ErrorMessages.AcmeChallengeTokenInvalid), ErrorType.UpstreamRefused));
        }

        return target switch
        {
            SiteDocumentRootChallenge site => await _files.DeleteEntryAsync(
                site.AccountUsername,
                ChallengePath(domain, token),
                recursive: false,
                cancellationToken),
            PanelChallengeDirectory => await _ssl.RemovePanelChallengeAsync(token, cancellationToken),
            _ => throw new UnreachableException("AcmeChallengeTarget is closed to the two targets this module declares."),
        };
    }

    /// <summary>Writes the file into the site's document root, as the owning account.</summary>
    /// <param name="site">The account whose document root answers.</param>
    /// <param name="domain">The domain being validated, which names its document root.</param>
    /// <param name="token">The challenge token, which is also the file name.</param>
    /// <param name="keyAuthorization">The file's content.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success, the floor's refusal, or <c>AcmeChallengeWriteFailed</c>.</returns>
    private async Task<Result<bool>> WriteIntoDocumentRootAsync(
        SiteDocumentRootChallenge site,
        string domain,
        string token,
        string keyAuthorization,
        CancellationToken cancellationToken)
    {
        var written = await _files.WriteFileAsync(
            site.AccountUsername,
            ChallengePath(domain, token),
            keyAuthorization,
            ChallengeFileMode,
            cancellationToken);

        return ChallengeFailure(
            written.IsSuccess ? Result<bool>.Ok(true) : Result<bool>.Fail(written.Error!),
            nameof(ErrorMessages.AcmeChallengeWriteFailed));
    }

    /// <summary>Words a failed challenge write as this module's failure, except the floor's refusal.</summary>
    /// <param name="written">What the agent answered the write with.</param>
    /// <param name="failureCode">
    /// This module's code for the place that could not be written: a site's document root, or the
    /// panel's own challenge directory — two places, two sentences.
    /// </param>
    /// <returns>
    /// Success unchanged; the agent's free-space floor refusal unchanged, so the operator reads "the
    /// server is nearly out of disk space" (issues #148, #132); every other failure as
    /// <paramref name="failureCode"/>, a local fault.
    /// </returns>
    private static Result<bool> ChallengeFailure(Result<bool> written, string failureCode)
    {
        if (written.IsSuccess || AgentFloorRefusal.Is(written.Error!))
        {
            return written;
        }

        return Result<bool>.Fail(Error.Of(failureCode, ErrorType.Failure));
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
