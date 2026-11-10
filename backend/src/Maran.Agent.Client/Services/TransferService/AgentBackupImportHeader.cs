namespace Maran.Agent.Client.Services.TransferService;

/// <summary>What an import carries before the first artifact byte: the signed manifest and the key the operator confirmed.</summary>
/// <param name="Manifest">The exact manifest bytes the sending agent signed.</param>
/// <param name="Signature">The signature over <paramref name="Manifest"/>.</param>
/// <param name="PublicKey">The signer's public key.</param>
/// <param name="ExpectedKeyFingerprint">
/// The lowercase hexadecimal SHA-256 of <paramref name="PublicKey"/> that the operator confirmed against the
/// sending server. Required: a signature alone proves nothing about who signed.
/// </param>
/// <param name="MaxBytes">The most artifact bytes the caller will receive, or 0 for the agent's own ceiling.</param>
public sealed record AgentBackupImportHeader(
    byte[] Manifest,
    byte[] Signature,
    byte[] PublicKey,
    string ExpectedKeyFingerprint,
    long MaxBytes);
