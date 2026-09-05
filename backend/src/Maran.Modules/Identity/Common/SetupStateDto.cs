namespace Maran.Modules.Identity.Common;

/// <summary>Whether the panel still needs its first administrator.</summary>
/// <param name="IsComplete">
/// True once any user exists. The SPA asks before deciding whether a login screen is even the right
/// thing to show a visitor.
/// </param>
public sealed record SetupStateDto(bool IsComplete);
