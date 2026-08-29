namespace Maran.Agent.Client.Services.SystemService;

/// <summary>Agent identity as seen by the backend (decoupled from wire types).</summary>
/// <param name="Version">Semantic version of the agent binary, e.g. "0.1.0".</param>
/// <param name="DistroId">/etc/os-release ID, e.g. "ubuntu", "almalinux".</param>
/// <param name="Family">Readable distro family: "debian", "rhel", or "unspecified".</param>
/// <param name="ProtoVersion">Highest contract revision the agent implements.</param>
public sealed record AgentInfoDto(string Version, string DistroId, string Family, uint ProtoVersion);
