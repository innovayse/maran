//! What one grant-repair pass looked at, changed and refused.

use crate::db::model::refused_grant::RefusedGrant;
use crate::db::model::repaired_grant::RepairedGrant;

/// The outcome of one `RepairDatabaseGrants` pass.
///
/// Every row of the server's grant table lands in exactly one of four places,
/// and the four add up to [`Self::examined`] — which is what makes the report
/// readable as an account of the whole table rather than a list of what happened
/// to be interesting.
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct GrantRepairReport {
    /// How many rows of the server's grant table were read and classified.
    pub examined: usize,

    /// How many rows already held the escaped form, or could not hold a wildcard
    /// at all.
    ///
    /// The two are counted together on purpose: for both, there is nothing to do
    /// and nothing for an operator to decide. It is what makes a second run
    /// observably a no-op — a clean install reports every row here, an empty
    /// [`Self::repaired`] and an empty [`Self::refused`].
    pub already_correct: usize,

    /// The rows that were rewritten, one entry each.
    ///
    /// Empty when the pass was asked to report only, in which case the same rows
    /// appear in [`Self::would_repair`].
    pub repaired: Vec<RepairedGrant>,

    /// The rows a report-only pass WOULD have rewritten, and did not.
    ///
    /// Always empty when the pass was allowed to act. It exists because this
    /// operation revokes and re-issues a customer's live database access, and an
    /// operator who cannot see the list before it happens has to trust the
    /// predicate instead of checking it.
    pub would_repair: Vec<RepairedGrant>,

    /// The rows that were deliberately left alone, with the reason for each.
    pub refused: Vec<RefusedGrant>,
}
