//! What one account's crontab can be observed to be doing right now.

/// The cron half of the evidence a caller needs before reporting a suspension.
///
/// Counted out of the crontab ITSELF, which is the only truth there is here:
/// the panel keeps no cron rows at all — the Cron module owns no entity — so a
/// check that consulted the database would answer green over a firing crontab.
/// That is the same defect the whole attestation exists to close, in a new
/// place.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct AccountCronSuspension {
    /// How many entries the panel manages in this account's crontab.
    pub entries_total: u32,

    /// How many of those carry the suspension marker on their installed line.
    ///
    /// Read off the LINE, never off the account-level marker the render writes
    /// beside the banner. The line is what decides whether cron can see the
    /// schedule, so it is the only thing worth observing; a marker that
    /// disagreed with the lines beneath it could otherwise certify a silence
    /// the file does not have.
    ///
    /// An entry the customer disabled themselves counts as UNSUSPENDED here.
    /// The two flags are two different facts, and a resume must give that entry
    /// back exactly as they left it.
    pub entries_suspended: u32,

    /// How many lines the crontab holds that this agent did not write.
    ///
    /// Carried so that a suspension can say what it did not silence. Suspension
    /// touches none of them — a crontab is not this agent's file, and an
    /// account with shell access or an administrator may add a line by hand —
    /// so they keep firing under a suspended account. Reporting the number is
    /// the honest answer; deleting them would destroy work nobody asked this
    /// agent to destroy.
    pub foreign_lines: u32,
}
