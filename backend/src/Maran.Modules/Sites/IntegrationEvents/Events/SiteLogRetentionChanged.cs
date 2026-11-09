namespace Maran.Modules.Sites.IntegrationEvents.Events;

/// <summary>
/// A site's log retention was just saved, so the archives beyond it can go (issue #152). Handled inside
/// this module by <c>SiteLogRetentionChangedHandler</c>.
/// </summary>
/// <remarks>
/// <para>
/// Published AFTER the row is committed and NOT enlisted in its transaction, and the loss that allows is
/// acceptable here: the hourly maintenance pass reads every site's stored number and prunes to it, so a
/// message lost in the window between the commit and the publish only delays the removal to the next
/// pass. Nothing is wrong in the meantime, because an archive that is kept a little longer than asked is
/// on the safe side of the installer's ceiling (rules/csharp.md "A message is published AFTER the commit").
/// </para>
/// <para>It carries no secret and only an id, so a durable queue would hold nothing sensitive.</para>
/// </remarks>
/// <param name="SiteId">The site whose retention changed.</param>
public sealed record SiteLogRetentionChanged(Guid SiteId);
