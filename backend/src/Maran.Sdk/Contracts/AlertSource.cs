namespace Maran.Sdk.Contracts;

/// <summary>Which module raised an <see cref="OperatorAlert"/>.</summary>
public enum AlertSource
{
    /// <summary>The Monitoring module: thresholds, service outages, panel updates.</summary>
    Monitoring = 1,

    /// <summary>The Backups module: failed, refused, missed and unverifiable backups.</summary>
    Backups = 2,

    /// <summary>The Ssl module: a certificate close to expiring.</summary>
    Ssl = 3,
}
