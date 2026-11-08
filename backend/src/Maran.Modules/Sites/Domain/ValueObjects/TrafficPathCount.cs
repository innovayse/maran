namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>How many requests of one day asked for one path.</summary>
/// <param name="Path">The request path with its query string already removed by the agent.</param>
/// <param name="Requests">How many requests asked for it.</param>
public sealed record TrafficPathCount(string Path, long Requests);
