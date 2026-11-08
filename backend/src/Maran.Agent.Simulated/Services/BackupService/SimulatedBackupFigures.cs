using System.Security.Cryptography;
using System.Text;
using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Simulated.State;

namespace Maran.Agent.Simulated.Services.BackupService;

/// <summary>
/// The sizes and digests a simulated backup reports: functions of the account, the backup id and the
/// databases at the moment of the backup, never of a random source or the time the code runs, so the
/// same actions in the same order describe the same artifact.
/// </summary>
public static class SimulatedBackupFigures
{
    /// <summary>The version the simulated agent writes into a manifest.</summary>
    public const string AgentVersion = "simulated";

    /// <summary>The manifest format version the simulated agent writes.</summary>
    public const uint ManifestVersion = 1;

    /// <summary>The smallest home directory a simulated account reports, in bytes.</summary>
    private const ulong SmallestHomeBytes = 20UL * 1024 * 1024;

    /// <summary>How many bytes above the smallest an account's home may reach.</summary>
    private const ulong HomeSpreadBytes = 100UL * 1024 * 1024;

    /// <summary>How much smaller than the data it holds an archive is: the home divides by this.</summary>
    private const ulong HomeCompressionRatio = 3;

    /// <summary>How much smaller than the data it holds a database dump is: its size divides by this.</summary>
    private const ulong DumpCompressionRatio = 4;

    /// <summary>The members every archive holds besides database dumps: the manifest and the home archive.</summary>
    private const uint FixedMembers = 2;

    /// <summary>Describes a backup of an account as it stands: its home, plus a dump of each database.</summary>
    /// <param name="account">System username of the account.</param>
    /// <param name="backupId">The backup identifier.</param>
    /// <param name="databases">The account's databases at the moment of the backup.</param>
    /// <param name="createdAt">The instant the backup is recorded at.</param>
    /// <returns>The readable artifact the agent would list.</returns>
    public static AgentReadableBackup Describe(
        string account,
        string backupId,
        IReadOnlyList<SimulatedDatabase> databases,
        DateTimeOffset createdAt)
    {
        var homeBytes = SmallestHomeBytes + (Number($"home:{account}") % HomeSpreadBytes);
        var dumps = databases
            .Select(database =>
            {
                return new AgentManifestDatabase(
                    database.FullDatabaseName,
                    database.SizeBytes,
                    Sha256($"dump:{database.FullDatabaseName}:{database.SizeBytes}"));
            })
            .ToList();
        var manifest = new AgentBackupManifest(
            ManifestVersion,
            account,
            backupId,
            createdAt.ToUnixTimeSeconds(),
            homeBytes,
            dumps,
            AgentVersion);
        var dumpBytes = dumps.Aggregate(0UL, (sum, dump) => { return sum + (dump.Bytes / DumpCompressionRatio); });
        var artifactBytes = (homeBytes / HomeCompressionRatio) + dumpBytes;

        return new AgentReadableBackup(manifest, artifactBytes, Sha256($"artifact:{account}:{backupId}:{homeBytes}"));
    }

    /// <summary>Counts the members an artifact's listing would hold.</summary>
    /// <param name="manifest">The artifact's manifest.</param>
    /// <returns>The manifest and the home archive, plus one dump per database the manifest names.</returns>
    public static uint MemberCount(AgentBackupManifest manifest)
    {
        return FixedMembers + (uint)manifest.Databases.Count;
    }

    /// <summary>Hashes a label into lowercase hex.</summary>
    /// <param name="label">What identifies the thing being digested.</param>
    /// <returns>Sixty-four lowercase hex digits.</returns>
    private static string Sha256(string label)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(label)));
    }

    /// <summary>
    /// Hashes a label into a number, for sizes that must differ between accounts but never change for one.
    /// </summary>
    /// <param name="label">What identifies the thing being sized.</param>
    /// <returns>The first eight bytes of the label's digest.</returns>
    private static ulong Number(string label)
    {
        return BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes(label)));
    }
}
