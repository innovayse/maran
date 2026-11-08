using System.Security.Cryptography.X509Certificates;

namespace Maran.Agent.Client.Models;

/// <summary>
/// The agent of a managed node, reached over TCP with mutual TLS 1.3 and no certificate authority:
/// each side recognises the other by the SHA-256 of its certificate.
/// </summary>
/// <param name="Host">The node's DNS name or IP address, as an administrator declared it.</param>
/// <param name="Port">The TCP port of the node's fleet listener.</param>
/// <param name="ClientCertificate">
/// The panel's own identity, with its private key. The endpoint source owns it and it is never
/// disposed here. Equality compares the certificate's content, so a replaced identity makes the
/// endpoint unequal to the one the pool built its channel for, and that is how the pool learns to
/// replace the channel.
/// </param>
/// <param name="PinnedServerFingerprint">
/// SHA-256 of the node certificate's DER encoding, 64 lowercase hex digits, recorded when an
/// administrator approved the node. The node is trusted if it presents exactly that certificate.
/// </param>
public sealed record MutualTlsAgentEndpoint(
    string Host,
    int Port,
    X509Certificate2 ClientCertificate,
    string PinnedServerFingerprint) : AgentEndpoint
{
    /// <summary>Identifies the endpoint without printing the certificate, so a log line cannot carry key material.</summary>
    /// <returns>Host, port and the pin.</returns>
    public override string ToString()
    {
        return $"{Host}:{Port} (pin {PinnedServerFingerprint})";
    }
}
