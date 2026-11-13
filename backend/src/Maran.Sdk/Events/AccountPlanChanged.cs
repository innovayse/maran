namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module AFTER an account's plan figures changed in a way a pool can
/// show: the account moved to another plan, or its plan's PHP bounds were edited (issue #138).
/// </summary>
/// <remarks>
/// <para>
/// <b>After the commit, and a failure here is not the change's failure.</b> The row is written; this
/// tells the modules that render something from the plan to render it again. A subscriber that cannot
/// finish (the agent is down) logs and returns, because nothing it could throw would undo a plan
/// change that already happened, and the panel's start-up reconciliation converges what was missed.
/// </para>
/// <para>
/// One event per affected account, so a subscriber never reads the Accounts schema to find out who
/// is on an edited plan.
/// </para>
/// </remarks>
/// <param name="AccountId">The account whose plan figures changed.</param>
/// <param name="UploadBoundChanged">
/// Whether the plan's upload bound (<see cref="Contracts.PhpLimitsSnapshot.UploadMaxMb"/>) differs from what the
/// account had before. The bound is the figure a PHP site's vhost request body limit follows, so a subscriber
/// that renders vhosts needs to act only when it moved; the other bounds change pools alone.
/// </param>
public sealed record AccountPlanChanged(Guid AccountId, bool UploadBoundChanged);
