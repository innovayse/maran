namespace Maran.Sdk.Events;

/// <summary>
/// What the suspension cascade knows about the session cull: exactly one of three readings, each a
/// type of its own so a count exists only on the reading that has one. Replaces a reported flag beside
/// a nullable count, whose null meant "reported, no count" only while the flag was true.
/// </summary>
/// <remarks>
/// The three are <see cref="SessionCullNotReported"/>, <see cref="SessionCullReportedWithoutCount"/>
/// and <see cref="SessionCullEnded"/>. A fourth reading would be a decision about what an operator's
/// attestation may claim, not something a caller invents by combining these.
/// </remarks>
public abstract record SessionCullReading;
