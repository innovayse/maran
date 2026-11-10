using System.Diagnostics;
using System.Text;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Writes this module's audit entries, and is the only place in the module that builds an
/// <see cref="AuditEntry"/>: it is where the module decides what an entry of its kind carries and, more
/// to the point, what it can never carry.
/// </summary>
/// <remarks>
/// <para>
/// <b>An entry says who, which module, which version, which package digest and why it was refused, and
/// nothing else.</b> The journal is append-only and never deleted (rules/security.md item 8), so what it
/// must not hold is enforced by the shape of the API and not by anyone's care: there is no parameter that
/// takes free text. The reason is a member of the closed <see cref="MarketplaceAuditReason"/> set, reached
/// from an <see cref="Error"/> by its code; the package is a <see cref="ModulePackageReference"/> of three
/// facts; the operation is a <see cref="MarketplaceOperation"/>. A lease, a grant id, a download address
/// or an exception message therefore has no way in.
/// </para>
/// <para>
/// <b>The three package facts are checked for shape before they are written.</b> They arrive from a route
/// or a response, so a module id, version or digest that is not what its name says is written as the word
/// <c>invalid</c> and never as the value it held: a URL passed as a module id leaves no trace of itself.
/// </para>
/// <para>
/// Every action and every refusal is recorded; a request that was refused is the entry worth reading.
/// Reads of the catalogue are not journalled per request, but a verification failure is.
/// </para>
/// </remarks>
public sealed class MarketplaceAuditJournal
{
    /// <summary>What a fact is written as when the entry has none to give.</summary>
    private const string Absent = "-";

    /// <summary>What a fact is written as when it was offered and is not what its name says.</summary>
    private const string Invalid = "invalid";

    /// <summary>The longest module id a manifest may carry.</summary>
    private const int ModuleIdMaxLength = 64;

    /// <summary>The length of a SHA-256 digest written as hexadecimal.</summary>
    private const int Sha256HexLength = 64;

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor of every entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the journal wrapper.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public MarketplaceAuditJournal(IAuditWriter auditWriter, ICurrentUser currentUser)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
    }

    /// <summary>Records an administrator switching the marketplace on or off, or being refused.</summary>
    /// <param name="enabled">The state that was asked for.</param>
    /// <param name="failure">Why it was refused, or null when it took effect.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordSwitchAsync(
        bool enabled,
        Error? failure,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        var action = enabled
            ? MarketplaceClientAuditActions.MarketplaceEnabled
            : MarketplaceClientAuditActions.MarketplaceDisabled;
        var subject = new StringBuilder(enabled ? "switch=on" : "switch=off");
        AppendReason(subject, failure);

        await WriteAsync(action, subject.ToString(), ipAddress, userAgent, failure is null, cancellationToken);
    }

    /// <summary>Records an administrator's request to install, update, remove or roll back a module, or its refusal.</summary>
    /// <param name="operation">What was asked.</param>
    /// <param name="package">The module and, where known, the version and digest it concerned.</param>
    /// <param name="failure">Why it was refused, or null when it was accepted.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordRequestAsync(
        MarketplaceOperation operation,
        ModulePackageReference package,
        Error? failure,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);

        var subject = new StringBuilder()
            .Append("module=").Append(ModuleIdText(package.ModuleId))
            .Append(";version=").Append(VersionText(package.Version))
            .Append(";digest=").Append(DigestText(package.PackageSha256));
        AppendReason(subject, failure);

        await WriteAsync(
            ActionOf(operation),
            subject.ToString(),
            ipAddress,
            userAgent,
            failure is null,
            cancellationToken);
    }

    /// <summary>Records that a marketplace answer failed verification and was not trusted.</summary>
    /// <param name="failure">Why it was not trusted: unsigned, stale, or no key to check it with.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    public async Task RecordVerificationFailedAsync(
        Error failure,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var subject = new StringBuilder("answer=catalogue");
        AppendReason(subject, failure);

        await WriteAsync(
            MarketplaceClientAuditActions.CatalogueVerificationFailed,
            subject.ToString(),
            ipAddress,
            userAgent,
            succeeded: false,
            cancellationToken);
    }

    /// <summary>The audit action constant for an operation.</summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The action name.</returns>
    private static string ActionOf(MarketplaceOperation operation)
    {
        return operation switch
        {
            MarketplaceOperation.Install => MarketplaceClientAuditActions.ModuleInstallRequested,
            MarketplaceOperation.Update => MarketplaceClientAuditActions.ModuleUpdateRequested,
            MarketplaceOperation.Remove => MarketplaceClientAuditActions.ModuleRemoveRequested,
            MarketplaceOperation.Rollback => MarketplaceClientAuditActions.ModuleRollbackRequested,
            _ => throw new UnreachableException("MarketplaceOperation has four members."),
        };
    }

    /// <summary>Appends <c>;reason=&lt;Name&gt;</c> when there is a failure.</summary>
    /// <param name="subject">The subject being built.</param>
    /// <param name="failure">The failure, or null for none.</param>
    private static void AppendReason(StringBuilder subject, Error? failure)
    {
        if (failure is not null)
        {
            subject.Append(";reason=").Append(ReasonOf(failure));
        }
    }

    /// <summary>Maps a failure to the closed reason vocabulary by the failure's code.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns>The reason whose name equals the code, or <see cref="MarketplaceAuditReason.Unclassified"/>.</returns>
    private static MarketplaceAuditReason ReasonOf(Error failure)
    {
        var known = Enum.TryParse<MarketplaceAuditReason>(failure.Code, ignoreCase: false, out var reason)
            && string.Equals(reason.ToString(), failure.Code, StringComparison.Ordinal)
            && reason != MarketplaceAuditReason.None;
        return known ? reason : MarketplaceAuditReason.Unclassified;
    }

    /// <summary>A module id as it may be written: lower-case letters and digits, starting with a letter.</summary>
    /// <param name="moduleId">The id offered.</param>
    /// <returns>The id itself, or <c>invalid</c>.</returns>
    private static string ModuleIdText(string moduleId)
    {
        var plain = moduleId.Length is > 0 and <= ModuleIdMaxLength
            && moduleId[0] is >= 'a' and <= 'z'
            && moduleId.All(character =>
            {
                return character is >= 'a' and <= 'z' or >= '0' and <= '9';
            });
        return plain ? moduleId : Invalid;
    }

    /// <summary>A version as it may be written: a semantic version the panel can parse.</summary>
    /// <param name="version">The version offered, or null.</param>
    /// <returns>The version itself, <c>-</c> for none, or <c>invalid</c>.</returns>
    private static string VersionText(string? version)
    {
        if (version is null)
        {
            return Absent;
        }

        return SemanticVersion.TryParse(version, out _) ? version : Invalid;
    }

    /// <summary>A digest as it may be written: sixty-four lower-case hexadecimal characters.</summary>
    /// <param name="digest">The digest offered, or null.</param>
    /// <returns>The digest itself, <c>-</c> for none, or <c>invalid</c>.</returns>
    private static string DigestText(string? digest)
    {
        if (digest is null)
        {
            return Absent;
        }

        var plain = digest.Length == Sha256HexLength
            && digest.All(character =>
            {
                return character is >= '0' and <= '9' or >= 'a' and <= 'f';
            });
        return plain ? digest : Invalid;
    }

    /// <summary>Builds and stores one entry under the signed-in caller's name.</summary>
    /// <param name="action">The action name.</param>
    /// <param name="subject">What the action concerned, built by this type from checked facts.</param>
    /// <param name="ipAddress">The caller's address.</param>
    /// <param name="userAgent">The caller's user agent.</param>
    /// <param name="succeeded">Whether the operation took effect.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Resolves once the entry is stored.</returns>
    private async Task WriteAsync(
        string action,
        string subject,
        string ipAddress,
        string userAgent,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var (actorId, actorName) = _currentUser.Caller switch
        {
            AdministratorCaller administrator => ((Guid?)administrator.UserId, administrator.Username),
            CustomerCaller customer => (customer.UserId, customer.Username),
            AccountlessCustomerCaller accountless => (accountless.UserId, accountless.Username),
            ResellerCaller reseller => (reseller.UserId, reseller.Username),
            ApiKeyCaller apiKey => (null, apiKey.ActorName),
            AnonymousCaller => (null, string.Empty),
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist."),
        };

        await _auditWriter.WriteAsync(
            new AuditEntry(actorId, actorName, action, subject, ipAddress, userAgent, succeeded),
            cancellationToken);
    }
}
