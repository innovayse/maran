namespace Maran.Host.Demo.Common;

/// <summary>
/// The fact a refused demo action publishes beside the problem's code: which area explains it and the
/// sentence for that area in the caller's language.
/// </summary>
/// <param name="Area">The area id, for example <c>securityPolicy</c> (<c>DemoRefusalAreaIds.IdOf</c>).</param>
/// <param name="Reason">The sentence for the area, localized.</param>
public sealed record DemoRefusalDto(string Area, string Reason);
