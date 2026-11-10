using Maran.SharedKernel.Security;

namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The key a call authenticates with, and the host key it requires the source to present.</summary>
/// <remarks>
/// This type is a <c>record</c>, whose compiler-generated <see cref="object.ToString"/> prints every
/// property, so the private key is held in <see cref="SensitiveString"/>: formatting a credential, logging
/// a request that carries one or letting an exception quote it prints <c>[redacted]</c>. The named test
/// <c>Formatting_a_credential_never_prints_the_private_key</c> holds it to that. The key is revealed once,
/// in <c>TransferRequestMapper</c>, when it is put on the wire.
/// </remarks>
/// <param name="PrivateKey">
/// The private half of the key the agent generated for this source, in the OpenSSH private key format,
/// exactly as <see cref="AgentGeneratedSourceKey.PrivateKey"/> returned it. A secret.
/// </param>
/// <param name="HostKey">
/// The pinned host key as one <c>&lt;algorithm&gt; &lt;base64&gt;</c> pair, exactly as
/// <see cref="AgentScannedHostKey.Key"/> returned it and the administrator confirmed it.
/// </param>
public sealed record AgentSourceCredential(SensitiveString PrivateKey, string HostKey);
