using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Common;
using Maran.Sdk.Contracts;

namespace Maran.Host.Demo.World;

/// <summary>A hosting plan the demo world adds to the three standard plans.</summary>
/// <param name="Name">The plan's name, also what accounts refer to it by.</param>
/// <param name="DiskQuotaMb">Disk quota in megabytes.</param>
/// <param name="MaxSites">Most sites an account may hold.</param>
/// <param name="MaxDatabases">Most databases an account may hold.</param>
/// <param name="MaxSftpUsers">Most SFTP logins an account may hold.</param>
/// <param name="MaxFtpUsers">Most FTPS logins an account may hold.</param>
/// <param name="MaxCronEntries">Most scheduled jobs an account may hold.</param>
/// <param name="MaxPhpWorkersPerPool">Most PHP workers per pool.</param>
/// <param name="Features">The features the plan sells.</param>
public sealed record DemoPlanDefinition(
    string Name,
    int DiskQuotaMb,
    int MaxSites,
    int MaxDatabases,
    int MaxSftpUsers,
    int MaxFtpUsers,
    int MaxCronEntries,
    int MaxPhpWorkersPerPool,
    IReadOnlyList<AccountFeature> Features)
{
    /// <summary>The plan as the create command states it, with every runtime ceiling unlimited.</summary>
    /// <param name="ipAddress">The origin recorded in the journal.</param>
    /// <param name="userAgent">The agent recorded in the journal.</param>
    /// <returns>The command that creates this plan.</returns>
    public CreatePlanCommand ToCommand(string ipAddress, string userAgent)
    {
        return new CreatePlanCommand(
            Name,
            DiskQuotaMb,
            MaxSites,
            MaxDatabases,
            MaxSftpUsers,
            MaxFtpUsers,
            MaxCronEntries,
            MaxPhpWorkersPerPool,
            new UnlimitedResourceCeilingDto(),
            new UnlimitedResourceCeilingDto(),
            new UnlimitedResourceCeilingDto(),
            Features,
            IpAddress: ipAddress,
            UserAgent: userAgent);
    }
}
