namespace Maran.Host.Demo.World;

/// <summary>One banned address of the demo world; the address comes from a documentation range.</summary>
/// <param name="Address">The banned address.</param>
/// <param name="DurationMinutes">How long the ban lasts, or null for a permanent one.</param>
public sealed record DemoBanDefinition(string Address, int? DurationMinutes);
