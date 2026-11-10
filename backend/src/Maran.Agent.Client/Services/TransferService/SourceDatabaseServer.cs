namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The database server found on a source.</summary>
/// <remarks>
/// "None found, or the probe could not run" has no value here; <see cref="AgentTestedSource.DatabaseServer"/>
/// is <c>null</c> for it.
/// </remarks>
public enum SourceDatabaseServer
{
    /// <summary>MariaDB.</summary>
    MariaDb = 0,

    /// <summary>MySQL.</summary>
    MySql = 1,
}
