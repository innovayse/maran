namespace Maran.Modules.Ssl.Models;

/// <summary>
/// The panel's own challenge: the file goes into the agent's directory that the panel's port-80
/// block serves <c>/.well-known/acme-challenge/</c> from (issue #132). It carries nothing, because
/// the directory is fixed on the agent and no caller may name it.
/// </summary>
public sealed record PanelChallengeDirectory : AcmeChallengeTarget;
