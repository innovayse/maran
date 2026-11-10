namespace Maran.Modules.Apps.Common;

/// <summary>The sites that stop an app from being removed, published beside the <c>AppsInUse</c> refusal under <c>sites</c>.</summary>
/// <param name="Total">How many sites use the app.</param>
/// <param name="Domains">The primary domains of the first of them, ascending; all of them when there are few.</param>
public sealed record AppInUseDto(int Total, IReadOnlyList<string> Domains);
