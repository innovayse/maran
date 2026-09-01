namespace Maran.Modules.Ssl.Common;

/// <summary>
/// The two machine-readable fields of an ACME problem document (RFC 8555 §6.7, RFC 7807): the HTTP
/// status and the error <c>type</c> URN.
/// </summary>
/// <remarks>
/// Deliberately NOT the <c>detail</c> member. <c>detail</c> is free prose written by the authority
/// and it quotes what the authority could not process — which, on a finalize, is the CSR, and on a
/// malformed request is whatever was sent. <c>type</c> is a fixed URN from a registry
/// (<c>…:error:badNonce</c>, <c>…:error:rateLimited</c>, <c>…:error:unauthorized</c>) and carries no
/// caller data at all, so it is the one part safe to write to a log (rules/security.md item 8).
///
/// It exists because an unattended renewal that keeps failing has to be diagnosable by an operator.
/// "The order was rejected" with nothing further is a support ticket nobody can close; "status 429,
/// type urn:ietf:params:acme:error:rateLimited" is an answer.
/// </remarks>
/// <param name="Status">The HTTP status the authority answered with.</param>
/// <param name="Type">The problem <c>type</c> URN, or the empty string when the body carried none.</param>
public sealed record AcmeProblem(int Status, string Type);
