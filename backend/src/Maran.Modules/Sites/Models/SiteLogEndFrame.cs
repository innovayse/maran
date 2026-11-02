using Maran.Modules.Sites.Domain.Enums;

namespace Maran.Modules.Sites.Models;

/// <summary>The frame that ends a stream.</summary>
/// <param name="Reason">Why the stream ended.</param>
/// <param name="Message">The localized sentence to show beside the ending, or <c>null</c> when it adds nothing.</param>
public sealed record SiteLogEndFrame(SiteLogEndReason Reason, string? Message) : SiteLogFrame;
