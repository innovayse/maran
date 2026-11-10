namespace Maran.Modules.Accounts.Models;

/// <summary>
/// What the Accounts module hands to another server for one account: the named fields below and nothing else.
/// It is the contract between the exporter on the sending server and the importer on the receiving one
/// (issue #127).
/// </summary>
/// <remarks>
/// No password, hash, session or token is a field of this record, and a column added to <c>Account</c> later is
/// not exported until someone adds it here. The plan is named for the administrator's information only: the
/// receiving administrator chooses a plan of the receiving server.
/// </remarks>
/// <param name="Name">The account's system user name, which the receiving server keeps (decision D1).</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="OwnerEmail">The owner's contact address.</param>
/// <param name="OwnerLanguage">The owner's language.</param>
/// <param name="PlanName">The name of the plan on the sending server, shown in the report.</param>
/// <param name="ShellAccess">Whether the account's system user had an interactive shell.</param>
public sealed record AccountsMigrationSection(
    string Name,
    string PrimaryDomain,
    string? OwnerEmail,
    string OwnerLanguage,
    string? PlanName,
    bool ShellAccess);
