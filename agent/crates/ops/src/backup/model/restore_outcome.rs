//! What a restore actually did, in the only shape that cannot round to "ok".

/// The result of one restore.
///
/// # There is no boolean `ok` here, and there is no `is_ok()`, and that is the
/// whole design
///
/// A caller cannot report this operation as successful without reading
/// [`Self::files_restored`], [`Self::databases_restored`] and
/// [`Self::databases_total`] and deciding for itself what they mean together.
/// That is not defensiveness about a hypothetical; it is the shape this
/// product's own worst reporting defect argues for. An account-deletion cascade
/// returned a value whose success was a single field, its caller read that
/// field, and the panel reported COMPLETED at 100% over rows the cascade had
/// never touched. Nothing in the type could see the difference between "did
/// everything" and "threw nothing", so nothing downstream could either.
///
/// A convenience method collapsing these fields into one boolean would restore
/// exactly the affordance this type was shaped to remove, and it would be used
/// — a caller reaches for the shortest expression that compiles. **Adding one
/// is a review reject.** The judgement belongs at the boundary that has to
/// state a verdict to a human, where it is one visible comparison
/// (`files_restored && databases_restored == databases_total`) sitting next to
/// the audit record it produces, rather than a method name three layers away.
///
/// # This IS the success value, and the failing paths answer in the error
///
/// Said plainly because this doc used to say the opposite at length — "a value
/// of this type is produced on failing paths too … that answer travels in
/// `Self::rolled_back` and `Self::not_rolled_back` beside the error" — and
/// nothing of the sort happened.
/// [`restore_backup`](crate::backup::restore_backup) returns
/// `Result<RestoreOutcome, BackupError>`, so a failing path returns `Err` and no
/// outcome at all; the two fields that paragraph was written about were filled
/// into a local value that was then dropped. A mutation emptying one of them
/// left the entire workspace suite green, because no caller could ever read it.
///
/// The requirement the paragraph described is real and is now met where the
/// value actually crosses back: [`BackupError::RolledBack`] carries
/// `rolled_back`, and [`BackupError::RolledBackPartially`] carries
/// `rolled_back` and `not_rolled_back`. An error alone would tell an operator
/// that something went wrong with a live account and nothing at all about which
/// half of it is now what — so the error carries it, and this type does not
/// carry two fields that could only ever be empty.
///
/// [`BackupError::RolledBack`]: crate::backup::BackupError::RolledBack
/// [`BackupError::RolledBackPartially`]: crate::backup::BackupError::RolledBackPartially
#[derive(Debug, Clone, PartialEq, Eq, Default)]
pub struct RestoreOutcome {
    /// Whether the account's home is now the archive's home.
    ///
    /// `true` only after BOTH renames of the swap have succeeded. A home parked
    /// aside with the staging tree not yet in place is not a restored home, and
    /// the window in which that is the state is measured in microseconds.
    pub files_restored: bool,

    /// How many databases were dropped, re-created and loaded from the archive.
    pub databases_restored: u32,

    /// How many databases the restore set out to replace — every database the
    /// manifest names, after the panel's `allowed_databases` has been applied.
    ///
    /// Equal to [`Self::databases_restored`] on a whole restore and larger on
    /// every other, which is the comparison a caller has to make.
    pub databases_total: u32,
}
