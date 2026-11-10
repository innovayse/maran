namespace Maran.Agent.Client.Services.TransferService;

/// <summary>The first event of an export: what a receiver verifies before it accepts a single artifact byte.</summary>
/// <param name="Manifest">The exact UTF-8 JSON bytes the agent signed. A receiver verifies over these and never re-serialises them.</param>
/// <param name="Signature">ECDSA P-384 / SHA-384 signature over <paramref name="Manifest"/>, ASN.1 DER encoded.</param>
/// <param name="PublicKey">The signer's public key as an uncompressed SEC1 point (97 bytes for P-384).</param>
public sealed record AgentBackupExportHeader(byte[] Manifest, byte[] Signature, byte[] PublicKey) : AgentBackupExportEvent;
