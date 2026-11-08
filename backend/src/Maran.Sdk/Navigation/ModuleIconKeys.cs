namespace Maran.Sdk.Navigation;

/// <summary>
/// The closed vocabulary of glyphs a module's menu entry may name. The SPA draws a neutral glyph for
/// a key it does not know, so an unknown key is not an error in the shell, but a module the repository
/// builds is held to this list.
/// </summary>
/// <remarks>
/// Each value is the key the SPA's navigation draws the glyph under (<c>NavigationIcon</c>); the
/// keys are presentation vocabulary shared with the shell, not domain values.
/// </remarks>
public static class ModuleIconKeys
{
    /// <summary>The dashboard, the panel's home.</summary>
    public const string House = "house";

    /// <summary>A module with no glyph of its own.</summary>
    public const string Grid = "grid";

    /// <summary>Accounts, plans and resellers.</summary>
    public const string Users = "users";

    /// <summary>Sites and the domains they answer on.</summary>
    public const string Earth = "earth";

    /// <summary>DNS zones.</summary>
    public const string Globe = "globe";

    /// <summary>Mailboxes and mail delivery.</summary>
    public const string Mail = "mail";

    /// <summary>Databases and their users.</summary>
    public const string Database = "database";

    /// <summary>File transfer logins and scheduled jobs: the ways into an account's files.</summary>
    public const string FolderKey = "folderKey";

    /// <summary>Backups.</summary>
    public const string Archive = "archive";

    /// <summary>The firewall and the sign-in policy.</summary>
    public const string ShieldCheck = "shieldCheck";

    /// <summary>The server fleet.</summary>
    public const string Server = "server";

    /// <summary>Metrics, logs, background tasks and the audit journal.</summary>
    public const string ChartLine = "chartLine";

    /// <summary>The panel's own settings.</summary>
    public const string Settings = "settings";

    /// <summary>A customer's own activity: the background tasks of their account.</summary>
    public const string ListChecks = "listChecks";

    /// <summary>A customer's own account: profile, sign-in security and sessions.</summary>
    public const string User = "user";
}
