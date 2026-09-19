//! What one `RepairHomeGroups` pass looked at, changed and refused.

use crate::accounts::model::refused_home::RefusedHome;
use crate::accounts::model::repaired_home::RepairedHome;

/// The outcome of one `RepairHomeGroups` pass.
///
/// Every hosting account on the host lands in exactly one of four places, and
/// the four add up to [`Self::examined`] — which is what makes the report
/// readable as an account of every account on the host rather than a list of
/// what happened to be interesting. The shape mirrors
/// `crate::db::model::grant_repair_report::GrantRepairReport`, which exists for
/// the same reason: a report-only pass is the only way into a repair that
/// changes something live, and the caller confirming it acts on a count it can
/// actually check.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct HomeGroupRepairReport {
    /// How many hosting accounts on this host were examined.
    ///
    /// A passwd row counts as a hosting account when its name parses as an
    /// [`maran_agent_core::validation::system::name::AccountName`] — nothing
    /// stronger, because whether its home is where this agent would have put
    /// it is one of the things the repair is deciding, not a precondition of
    /// being counted.
    pub examined: usize,

    /// How many accounts' homes were already group-owned by the web server's
    /// group.
    ///
    /// Counted rather than listed: there is nothing to do and nothing for an
    /// operator to decide about any of them, and a clean host with the
    /// account-creation fix already in place reports every account here — an
    /// empty [`Self::repaired`], [`Self::would_repair`] and [`Self::refused`]
    /// alike, so a second run is observably a no-op.
    pub already_correct: usize,

    /// The homes that were re-grouped, one entry each.
    ///
    /// Empty when the pass was asked to report only, in which case the same
    /// accounts appear in [`Self::would_repair`] instead.
    pub repaired: Vec<RepairedHome>,

    /// The homes a report-only pass WOULD have re-grouped, and did not.
    ///
    /// Always empty when the pass was allowed to act. This exists because the
    /// repair changes who can traverse into a customer's home, across every
    /// account on the host at once, and an operator who cannot see the list
    /// before it happens has to trust the predicate instead of checking it.
    pub would_repair: Vec<RepairedHome>,

    /// The accounts whose homes were deliberately left alone, with the reason
    /// for each.
    pub refused: Vec<RefusedHome>,
}
