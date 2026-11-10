using Maran.SharedKernel.Apps;

namespace Maran.SharedKernel.Interfaces;

/// <summary>
/// What the app store has installed for sites: the one site web server and the runtimes sites can run on
/// (issue #517). The contract lives in the SharedKernel and its implementation in the Apps module that owns
/// the ledger, because a module may never reference another module (rules/architecture.md "Backend:
/// modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// This is the enforcement of the first-run gate: the Sites module reads it before creating or changing a
/// site and answers <c>Conflict</c> <c>AppsStackNotReady</c> while no web server is installed, and
/// <c>AppsNotInstalled</c> for a runtime that is not. The SPA only reflects it (rules/security.md). The
/// answer is server-wide and carries no tenant data, so it needs no scope.
/// </para>
/// <para>
/// The members are properties, read once per request scope: the implementation takes one consistent
/// reading of the ledger, so a handler that asks several questions of one request gets one answer.
/// </para>
/// </remarks>
public interface IStackStatus
{
    /// <summary>Whether a site web server is installed. While it is not, no site can be created or changed.</summary>
    bool IsReady { get; }

    /// <summary>The installed site web server, or <see cref="StackWebServer.None"/> while none is.</summary>
    StackWebServer WebServer { get; }

    /// <summary>The installed PHP versions as the agent names them (<c>8.3</c>), newest first.</summary>
    IReadOnlyList<string> PhpVersions { get; }

    /// <summary>The installed Node.js major versions (<c>22</c>), newest first.</summary>
    IReadOnlyList<string> NodeMajors { get; }
}
