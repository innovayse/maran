namespace Maran.Sdk.Events;

/// <summary>
/// A subscriber ran the cull and the HOST's answer carried no count — an agent predating the wire
/// field. The publisher may say the cull was done but not how many sessions it ended, nor that any
/// were.
/// </summary>
public sealed record SessionCullReportedWithoutCount : SessionCullReading;
