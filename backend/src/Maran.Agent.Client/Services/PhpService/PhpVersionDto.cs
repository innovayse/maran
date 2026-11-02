namespace Maran.Agent.Client.Services.PhpService;

/// <summary>One PHP runtime installed on the server.</summary>
/// <param name="Version">Two-component version as the packages name it, e.g. <c>8.3</c>.</param>
/// <param name="FpmSocketDirectory">
/// Absolute path to this version's FPM socket directory, so a site's vhost can be pointed at the
/// right pool.
/// </param>
/// <param name="DefaultStatus">
/// Whether this version is the host's default CLI PHP. <see cref="AgentPhpDefaultStatus.Unknown"/>
/// when the agent did not establish it, which is a different answer from
/// <see cref="AgentPhpDefaultStatus.NotDefault"/>: the agent does not currently determine the
/// default, and "not known" must not be rendered as "not the default".
/// </param>
public sealed record PhpVersionDto(string Version, string FpmSocketDirectory, AgentPhpDefaultStatus DefaultStatus);
