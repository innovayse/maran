namespace Maran.Modules.Sites.Domain.ValueObjects;

/// <summary>How many requests of one day were answered with one HTTP status code.</summary>
/// <param name="Code">The HTTP status code.</param>
/// <param name="Requests">How many requests were answered with it.</param>
public sealed record TrafficStatusCount(int Code, long Requests);
