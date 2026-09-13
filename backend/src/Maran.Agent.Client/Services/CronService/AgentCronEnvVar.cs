namespace Maran.Agent.Client.Services.CronService;

/// <summary>One environment assignment the agent manages in an account's crontab.</summary>
/// <param name="Name">
/// The variable's name. The agent accepts letters, digits and underscores not starting with a digit,
/// and refuses <c>MAILTO</c> and <c>SHELL</c>: the first is an outbound-relay primitive through the
/// host's mail transfer agent and the second changes the interpreter under every managed entry. It
/// emits both itself, with values of its own choosing.
/// </param>
/// <param name="Value">
/// The value, written verbatim. A crontab is line-oriented, so the agent refuses a newline, a
/// carriage return, a NUL and any other control character.
/// </param>
/// <remarks>
/// Assignments written by the account or by the host outside the agent's own region of the crontab
/// are neither reported nor rewritten, so this type describes the managed region only.
/// </remarks>
public sealed record AgentCronEnvVar(string Name, string Value);
