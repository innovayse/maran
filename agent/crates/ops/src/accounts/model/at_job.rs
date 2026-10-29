//! One queued `at` job as `atq` lists it.

/// A queued `at` job: its numeric id and the user it will run as.
///
/// Built only by `parse_atq_listing`, which guarantees `id` is a non-empty run
/// of ASCII digits, so it can be handed to `atrm` as a plain argument without
/// ever being read as an option.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AtJob {
    /// The job number `atq` prints in its first column.
    pub id: String,

    /// The user the job runs as, the last column of the listing.
    pub user: String,
}
