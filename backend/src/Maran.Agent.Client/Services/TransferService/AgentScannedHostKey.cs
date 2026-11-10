namespace Maran.Agent.Client.Services.TransferService;

/// <summary>One host key a source offered.</summary>
/// <param name="Algorithm">The algorithm name, e.g. <c>ssh-ed25519</c> or <c>ecdsa-sha2-nistp256</c>.</param>
/// <param name="Key">The key as the <c>&lt;algorithm&gt; &lt;base64&gt;</c> pair a known_hosts line carries.</param>
/// <param name="Fingerprint">The <c>SHA256:&lt;base64&gt;</c> fingerprint ssh prints for it.</param>
public sealed record AgentScannedHostKey(string Algorithm, string Key, string Fingerprint);
