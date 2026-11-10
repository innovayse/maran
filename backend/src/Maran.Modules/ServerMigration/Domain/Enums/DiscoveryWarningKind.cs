namespace Maran.Modules.ServerMigration.Domain.Enums;

/// <summary>The closed set of things a discovery can report it could not read, or chose not to report in full.</summary>
public enum DiscoveryWarningKind
{
    /// <summary>A name on the source is not one the panel accepts, so the item was left out.</summary>
    NameRefused,

    /// <summary>A site's web server configuration could not be read.</summary>
    SiteUnreadable,

    /// <summary>An account's scheduled jobs could not be read.</summary>
    CronUnreadable,

    /// <summary>The list of databases could not be read.</summary>
    DatabasesUnreadable,

    /// <summary>The disk use of a home directory could not be measured.</summary>
    DiskUnreadable,

    /// <summary>A bound was reached and the inventory stops there.</summary>
    Truncated,

    /// <summary>The source holds mail, which this tool counts and never migrates.</summary>
    MailNotMigrated,
}
