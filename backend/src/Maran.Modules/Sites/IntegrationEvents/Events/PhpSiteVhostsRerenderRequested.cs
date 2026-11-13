namespace Maran.Modules.Sites.IntegrationEvents.Events;

/// <summary>
/// An account's plan upload bound moved, so the vhosts of its enabled PHP sites must be rendered again to
/// carry the new request body limit (issues #138 and #556). Handled inside this module by
/// <c>PhpSiteVhostsRerenderRequestedHandler</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is a message of its own and not work in the plan change's handler.</b> The plan change is
/// announced inside the administrator's HTTP request, once per account on an edited plan, and a render is
/// one agent call per site. Doing that inline would hold the request for the whole fan-out and let a
/// cancelled request stop it half way. The pool write stays inline (one call per account); the vhosts are
/// queued.
/// </para>
/// <para>
/// Published AFTER the plan change is committed and NOT enlisted in its transaction, and the loss that allows
/// is acceptable: the panel's start-up pass renders every PHP site again from the same stored rows
/// (<c>StartupPhpSiteReconciler</c>), so a lost message only delays the new bound to the next start. It
/// carries an id only, so a durable queue holds nothing sensitive.
/// </para>
/// </remarks>
/// <param name="AccountId">The account whose PHP sites are rendered again.</param>
public sealed record PhpSiteVhostsRerenderRequested(Guid AccountId);
