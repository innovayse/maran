namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for hosting account lifecycle, plan, deletion and final-backup actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class AccountAuditActions
{
    /// <summary>
    /// A hosting account was created: its Linux user, home directory and disk quota now exist on
    /// the host. Recorded on the refusals too, because a name or a domain already taken is how one
    /// caller learns that another customer holds it.
    /// </summary>
    public const string AccountCreated = "AccountCreated";

    /// <summary>
    /// An account was suspended: its sites and services stopped while its data stayed. The event a
    /// customer whose sites went dark needs explained, and the one a billing system produces.
    /// </summary>
    public const string AccountSuspended = "AccountSuspended";

    /// <summary>An account's suspension was lifted and its sites and services put back.</summary>
    public const string AccountReactivated = "AccountReactivated";

    /// <summary>
    /// An account was moved to another plan, and with it its disk quota on the host. Recorded on the
    /// refusals too — above all the refusal because the account does not fit the new plan's quota.
    /// The subject is the account's name and both plans, never a translation.
    /// </summary>
    public const string AccountPlanChanged = "AccountPlanChanged";

    /// <summary>
    /// An account's owner address was changed, and with it the address its login's invitation and
    /// password resets go to. The subject is the account's name; the addresses are not journalled,
    /// because the journal is never deleted and an address is personal data.
    /// </summary>
    public const string AccountOwnerEmailChanged = "AccountOwnerEmailChanged";

    /// <summary>
    /// An account was deleted, and with it its system user, its home directory, every database it
    /// owned and every file-transfer login it owned, of either daemon. The most destructive
    /// operation the panel offers and the
    /// one nothing else leaves a trace of: after it, the account name is all that is left to search
    /// for. Recorded on the refusals too — a deletion that got part-way through the cascade and then
    /// failed is journalled as the failure it was.
    /// </summary>
    public const string AccountDeleted = "AccountDeleted";

    /// <summary>
    /// The final backup promised before an account is destroyed (spec §12) was attempted. Recorded
    /// on the refusals too, and the refusal is the entry that matters: it is the record of a
    /// deletion that was abandoned because the customer's data could not be archived first.
    /// </summary>
    public const string FinalBackupTaken = "FinalBackupTaken";

    /// <summary>
    /// An account was deleted WITHOUT its final backup, at an administrator's explicit request.
    /// </summary>
    /// <remarks>
    /// Its own action rather than a flag on the deletion, because it answers a question a deletion
    /// entry cannot: who decided that this customer's data did not need keeping, and when. It is the
    /// entry an operator looks for when a customer asks for their files back after the account is
    /// gone.
    /// </remarks>
    public const string FinalBackupSkipped = "FinalBackupSkipped";

    /// <summary>
    /// An account was deleted with no final backup because this panel has no Backups module.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT the same action as <see cref="FinalBackupSkipped"/>. "Nobody chose this" and
    /// "an administrator chose this" are different facts about why no copy exists, and folding them
    /// into one entry would make them the same observation — which is the exact substitution that
    /// produced the deletion defects this journal exists to have caught.
    /// </remarks>
    public const string FinalBackupSkippedNoModule = "FinalBackupSkippedNoModule";

    /// <summary>
    /// Every hosting account's home directory group was classified and, where wrong, re-grouped to the
    /// web server's group (issue #28 item E). Recorded on the refusals too, and for the same reason
    /// <see cref="DatabaseAuditActions.DatabaseGrantsRepaired"/>'s refusals are: an operator who confirmed a figure that no
    /// longer matched the host — an ordinary race, or an attempt to act without reading a fresh report
    /// — is a pattern only the journal makes visible.
    /// <para>
    /// The subject is a count, never an account name. Several of the refusal reasons this operation can
    /// report — an owner mismatch, a home on a different mount — exist precisely because the row at a
    /// given path may not be the account the panel's own naming expects, so a name in this trail could
    /// be wrong in a way nothing here can detect. The counts are current, verifiable, and — like
    /// <see cref="DatabaseAuditActions.DatabaseGrantsRepaired"/>'s subject — this journal is never deleted (rules/security.md).
    /// </para>
    /// </summary>
    public const string AccountHomeGroupsRepaired = "AccountHomeGroupsRepaired";

    /// <summary>
    /// A plan was defined. Recorded on the refusals too: a name already taken is a conflict an
    /// operator wants to find later, and a creation the validator accepted but the handler refused is
    /// the half of the trail that explains a plan that is missing.
    /// </summary>
    public const string PlanCreated = "PlanCreated";

    /// <summary>
    /// A plan's name or limits were changed, and with them the limits of every account already on it.
    /// Recorded on the refusals too — above all the refusal to lower a disk quota under an account's
    /// current usage, which is the one an operator who then asks "why did my edit not take" needs.
    /// </summary>
    public const string PlanUpdated = "PlanUpdated";

    /// <summary>
    /// A plan was deleted. Recorded on the refusals too: the refusal because accounts still sit on the
    /// plan is how an operator learns who is still being sold it.
    /// </summary>
    public const string PlanDeleted = "PlanDeleted";
}
