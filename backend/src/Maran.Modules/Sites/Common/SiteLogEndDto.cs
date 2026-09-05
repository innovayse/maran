using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Common;

/// <summary>The payload of the single <c>end</c> event that closes every site-log stream.</summary>
/// <param name="Reason">Why the stream ended — never omitted, never guessed.</param>
/// <param name="Message">
/// The already-localized sentence to show beside the reason, or <c>null</c> when the reason speaks
/// for itself. The backend owns every word a customer reads (rules/csharp.md), so this is resolved
/// from the module's resources here and never assembled in the SPA. It is a resource sentence and
/// never the agent's own text, which can name absolute paths on the host.
/// </param>
public sealed record SiteLogEndDto(SiteLogEndReason Reason, string? Message);
