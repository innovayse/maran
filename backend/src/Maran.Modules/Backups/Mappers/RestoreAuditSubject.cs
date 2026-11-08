using Maran.Agent.Client.Services.BackupService;

namespace Maran.Modules.Backups.Mappers;

/// <summary>
/// States what a restore's audit entry is about: the account, and the one part of it when the restore
/// was narrowed to a part (issue #160).
/// </summary>
/// <remarks>
/// A mapper translates and never decides (rules/csharp.md): the scope arrives already established by the
/// handler, and this only restates it as the text an operator searches for. The account comes first and
/// the part after a separator, so a search for the account finds every restore of it whatever it
/// replaced, and a search for the part finds the narrowed ones. The part is a database name or a
/// domain — both names the panel itself holds, never a path: an entry naming where a customer's files
/// sit would be a permanent index of exactly that (rules/security.md item 8).
/// </remarks>
public static class RestoreAuditSubject
{
    /// <summary>Restates a restore's target as an audit subject.</summary>
    /// <param name="accountUsername">The account being restored.</param>
    /// <param name="scope">What the restore replaces.</param>
    /// <returns>
    /// The account name alone for a whole restore, <c>alice/database/alice_shop</c> for one database and
    /// <c>alice/site/shop.example.com</c> for one site.
    /// </returns>
    public static string Of(string accountUsername, AgentRestoreScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return scope switch
        {
            AgentDatabaseRestoreScope database => $"{accountUsername}/database/{database.DatabaseName}",
            AgentSiteRestoreScope site => $"{accountUsername}/site/{site.Domain}",
            _ => accountUsername,
        };
    }
}
