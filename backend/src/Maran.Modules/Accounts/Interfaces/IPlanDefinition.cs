namespace Maran.Modules.Accounts.Interfaces;

/// <summary>
/// What an administrator states when defining or editing a plan: its name and every limit it sells.
/// </summary>
/// <remarks>
/// Creating a plan and editing one take exactly the same fields, and the rules about what a field may
/// hold are the same too, so both commands implement this and share one validator over it
/// (<see cref="Validators.PlanDefinitionValidator{TCommand}"/>) instead of two copies that could come
/// to disagree about what a sellable plan is.
/// </remarks>
public interface IPlanDefinition
{
    /// <summary>The plan's name, as an operator wants it read.</summary>
    string Name { get; }

    /// <summary>The account's disk quota, in megabytes.</summary>
    int DiskQuotaMb { get; }

    /// <summary>The maximum number of sites the account may create.</summary>
    int MaxSites { get; }

    /// <summary>The maximum number of databases the account may create.</summary>
    int MaxDatabases { get; }

    /// <summary>The maximum number of SFTP logins the account may create.</summary>
    int MaxSftpUsers { get; }

    /// <summary>The maximum number of FTPS logins the account may create.</summary>
    int MaxFtpUsers { get; }

    /// <summary>The maximum number of cron entries the account may keep.</summary>
    int MaxCronEntries { get; }

    /// <summary>The maximum number of php-fpm workers one of the account's pools may run.</summary>
    int MaxPhpWorkersPerPool { get; }
}
