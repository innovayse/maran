namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The steps of an inventory read, in the order they run.</summary>
public enum SourceInventoryStep
{
    /// <summary>Reading the list of system accounts.</summary>
    Accounts = 0,

    /// <summary>Reading the web server's virtual hosts.</summary>
    Sites = 1,

    /// <summary>Reading the database server's databases.</summary>
    Databases = 2,

    /// <summary>Reading the accounts' crontabs.</summary>
    Cron = 3,

    /// <summary>Measuring the accounts' homes.</summary>
    Disk = 4,
}
