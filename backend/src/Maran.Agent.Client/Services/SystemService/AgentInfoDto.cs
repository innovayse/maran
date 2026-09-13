namespace Maran.Agent.Client.Services.SystemService;

/// <summary>Agent identity as seen by the backend (decoupled from wire types).</summary>
/// <param name="Version">Semantic version of the agent binary, e.g. "0.1.0".</param>
/// <param name="DistroId">/etc/os-release ID, e.g. "ubuntu", "almalinux".</param>
/// <param name="Family">Readable distro family: "debian", "rhel", or "unspecified".</param>
/// <param name="ProtoVersion">Highest contract revision the agent implements.</param>
/// <param name="BackupRoot">
/// The directory the agent writes account backup archives into, as the agent itself states it;
/// empty when the agent predates the field. It is carried here rather than restated anywhere in the
/// panel because there is exactly one statement of it — the agent's constant — and a caller that
/// receives an empty value must say it does not know the path instead of showing a default.
/// </param>
public sealed record AgentInfoDto(
    string Version,
    string DistroId,
    string Family,
    uint ProtoVersion,
    string BackupRoot);
