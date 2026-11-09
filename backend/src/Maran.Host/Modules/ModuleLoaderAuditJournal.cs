using System.Diagnostics;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.Sdk.Manifests;
using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Utilities.Network;
using Maran.SharedKernel.Utilities.Versions;

namespace Maran.Host.Modules;

/// <summary>
/// Writes the audit entries for what the module loader does and what it refuses, so that every step of
/// the lifecycle of a module that arrived as a package is recorded in one shape (issues #184, #188).
/// </summary>
/// <remarks>
/// <para>
/// <b>It lives in the Host, and why that is not a module's journal.</b> The loader is a separate
/// package that cannot reference the Host or any module, so it reaches the journal through the Sdk's
/// <see cref="IModuleLoaderAudit"/>; the entry is built here, in one file that decides what such an
/// entry may carry (rules/csharp.md "A module writes its audit entries through its own journal"). The
/// Host owns the type because no module owns the marketplace: the facility is not one module's.
/// </para>
/// <para>
/// <b>Who the actor is.</b> An administrator who asked for the step, under their name and with the
/// address and agent of the request; an API key, under the key's actor name. A step nobody asked for -
/// a lease lapsing, a module refused at start - has no signed-in caller and is recorded as the panel
/// itself (<c>maran-marketplace</c>), with an empty address, because nothing arrived over HTTP.
/// </para>
/// <para>
/// <b>What a subject may carry.</b> A refusal is written about a package that FAILED verification, so
/// the id, version and reason arriving here may be anything its author typed. The subject is built from
/// a module id only if it is well formed (<see cref="ManifestValidator.IsWellFormedId"/>), a version
/// only if it parses (<see cref="SemanticVersion"/>) and a reason only if it is a plain code of ASCII
/// letters and digits; a part that fails its test is left out, and a subject with no usable part reads
/// <c>module=unreadable</c>. No path, URL, exception text or free-form sentence ever reaches the
/// journal, which is append-only and never deleted (rules/security.md item 8).
/// </para>
/// </remarks>
public sealed class ModuleLoaderAuditJournal : IModuleLoaderAudit
{
    /// <summary>This facility's short name, from which its system actor name is built.</summary>
    public const string ModuleName = "marketplace";

    /// <summary>The subject of an entry about a package whose manifest could not be read at all.</summary>
    public const string UnreadableSubject = "module=unreadable";

    /// <summary>The longest reason code written to a subject.</summary>
    private const int MaxReasonLength = 64;

    /// <summary>The panel's append-only journal.</summary>
    private readonly IAuditWriter _auditWriter;

    /// <summary>The authenticated principal, recorded as the actor.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The request in progress, if any, for the address and agent.</summary>
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Creates the journal.</summary>
    /// <param name="auditWriter">The panel's append-only journal.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="httpContextAccessor">Gives the current request, absent for work with no request.</param>
    public ModuleLoaderAuditJournal(
        IAuditWriter auditWriter,
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor)
    {
        _auditWriter = auditWriter;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        string action,
        string? moduleId,
        string? version,
        Error? reason,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var subject = SubjectOf(moduleId, version, reason);

        Guid? userId = null;
        var username = SystemAuditEntry.NameFor(ModuleName);
        var viaRequest = false;

        switch (_currentUser.Caller)
        {
            case PrincipalCaller principal:
                userId = principal.UserId;
                username = principal.Username;
                viaRequest = true;
                break;
            case ApiKeyCaller apiKey:
                username = apiKey.ActorName;
                viaRequest = true;
                break;
            case AnonymousCaller:
                break;
            default:
                throw new UnreachableException("Caller is sealed to SharedKernel: six cases exist.");
        }

        var request = viaRequest ? _httpContextAccessor.HttpContext : null;
        var address = request is null ? string.Empty : ClientAddress.Of(request.Connection.RemoteIpAddress);
        var agent = request is null ? string.Empty : UserAgentText.Capped(request.Request.Headers.UserAgent.ToString());

        await _auditWriter.WriteAsync(
            new AuditEntry(userId, username, action, subject, address, agent, succeeded),
            cancellationToken);
    }

    /// <summary>Builds the subject from the parts that are safe to write.</summary>
    /// <param name="moduleId">The id the manifest claimed, or null.</param>
    /// <param name="version">The version the manifest claimed, or null.</param>
    /// <param name="reason">The failure, or null.</param>
    /// <returns><c>module=..;version=..;reason=..</c> with the unusable parts left out, or <see cref="UnreadableSubject"/>.</returns>
    private static string SubjectOf(string? moduleId, string? version, Error? reason)
    {
        var parts = new List<string>();

        if (moduleId is not null && ManifestValidator.IsWellFormedId(moduleId))
        {
            parts.Add("module=" + moduleId);
        }

        if (version is not null && SemanticVersion.TryParse(version, out _))
        {
            parts.Add("version=" + version);
        }

        if (reason is not null && IsPlainCode(reason.Code))
        {
            parts.Add("reason=" + reason.Code);
        }

        return parts.Count == 0 ? UnreadableSubject : string.Join(';', parts);
    }

    /// <summary>Whether an error code is made only of ASCII letters and digits and is short enough to record.</summary>
    /// <param name="code">The code.</param>
    /// <returns>True when it may be written to a subject.</returns>
    private static bool IsPlainCode(string code)
    {
        return code.Length is > 0 and <= MaxReasonLength
            && code.All(character => { return char.IsAsciiLetterOrDigit(character); });
    }
}
