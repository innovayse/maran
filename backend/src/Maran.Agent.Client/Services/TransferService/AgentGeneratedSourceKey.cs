using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One freshly generated ed25519 key pair for a remote source.</summary>
/// <remarks>
/// The agent keeps nothing of it. The panel stores the private half encrypted and sends it back on every
/// later call; the private half is a <see cref="SensitiveString"/> for the reason
/// <see cref="AgentSourceCredential"/> states.
/// </remarks>
/// <param name="PublicKey">The public half in the <c>authorized_keys</c> form, <c>ssh-ed25519 &lt;base64&gt;</c>, with no comment and no option.</param>
/// <param name="PrivateKey">The private half in the OpenSSH private key format. A secret; never returned by the panel.</param>
/// <param name="Fingerprint">The fingerprint of the public half in the <c>SHA256:&lt;base64&gt;</c> form ssh prints.</param>
public sealed record AgentGeneratedSourceKey(string PublicKey, SensitiveString PrivateKey, string Fingerprint);
