namespace Maran.Sdk.Contracts;

/// <summary>
/// A capability of the panel that a plan may include or leave out, held as a set: an account's plan
/// carries one <see cref="AccountFeature"/> value whose bits are the features it sells.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only features the panel has, or the tree plans, are members.</b> A name that nothing implements
/// would be a checkbox on the plan form that sells nothing, so a member is added in the change that
/// adds the thing it names.
/// </para>
/// <para>
/// <b>Who enforces what.</b> The Accounts module stores the set on the plan. Every customer-reachable
/// controller of the module that owns the feature opts in with <c>[RequiresAccountFeature]</c>
/// (<see cref="Filters.RequiresAccountFeatureAttribute"/>), so the refusal does not depend on the
/// menu hiding anything. <see cref="Mail"/> and <see cref="Dns"/> have no module in the tree yet:
/// the members exist so a plan can already state them and the menu can already filter on them, and
/// the module that arrives adds its one attribute line.
/// </para>
/// <para>
/// The set travels as an array of names on the wire (never as the bits), so a client does not decode
/// anything and a new member needs no client change to be listed. <see cref="None"/> is the empty
/// set and <see cref="All"/> the full one; neither is a feature, and neither appears in a list of
/// them (<see cref="Extensions.AccountFeatureExtensions.Members"/>).
/// </para>
/// </remarks>
[Flags]
public enum AccountFeature : long
{
    /// <summary>The empty set: a plan that includes none of the features.</summary>
    None = 0,

    /// <summary>Mailboxes and mail delivery for the account's domains. No module yet.</summary>
    Mail = 1L << 0,

    /// <summary>DNS zones for the account's domains. No module yet.</summary>
    Dns = 1L << 1,

    /// <summary>The account's databases, served by the Databases module.</summary>
    Databases = 1L << 2,

    /// <summary>The account's scheduled tasks, served by the Cron module.</summary>
    Cron = 1L << 3,

    /// <summary>The account's file-transfer logins over SFTP and FTPS, served by the Sftp and Ftp modules.</summary>
    FileTransfer = 1L << 4,

    /// <summary>The account's backups, served by the Backups module.</summary>
    Backups = 1L << 5,

    /// <summary>The full set: every feature above. What a plan that predates the set, and every shipped plan, includes.</summary>
    All = Mail | Dns | Databases | Cron | FileTransfer | Backups,
}
