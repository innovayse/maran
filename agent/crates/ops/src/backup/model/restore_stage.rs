//! The named stages a restore reports progress under.

/// One stage of a restore, and the span of the overall percentage it owns.
///
/// A separate enum from [`crate::backup::model::backup_stage::BackupStage`] and
/// not extra variants on it. The two operations report different work over
/// different spans, and a single enum would let a creation emit
/// `restoring_databases` — a stage name is a claim about what is happening to a
/// live account, and the type is the cheapest place to make that claim
/// unrepresentable.
///
/// The spans are FIXED and are part of the operation's contract with whoever
/// watches it: `verifying` owns 0→10, `downloading` 10→30,
/// `restoring_databases` 30→70, `restoring_files` 70→95, `finalising` 95→99,
/// and 100 belongs to the terminal message alone. A fixed span is what makes a
/// stall legible: an operation stuck at 45% is stuck partway through replacing
/// databases, which is a state an operator can go and look at.
///
/// The stage names are the wire names, lowercase with underscores, because the
/// service layer copies them into the stream verbatim.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RestoreStage {
    /// The artifact's digest, its manifest and its member list are being
    /// checked. Nothing has been touched while this stage is reported.
    Verifying,

    /// The artifact's bytes are being fetched from a remote destination.
    ///
    /// A restore from a local destination never reports this stage: there is
    /// nothing to download, and reporting a stage that did no work is the kind
    /// of progress this product has already been burned by.
    Downloading,

    /// Databases are being dumped for rollback, dropped and reloaded. **The
    /// point of no return falls inside this stage.**
    RestoringDatabases,

    /// The archive's home is being extracted and swapped into place.
    RestoringFiles,

    /// Ownership and mode are being re-applied and the staging trees removed.
    Finalising,
}

impl RestoreStage {
    /// The stage's wire name.
    #[must_use]
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Verifying => "verifying",
            Self::Downloading => "downloading",
            Self::RestoringDatabases => "restoring_databases",
            Self::RestoringFiles => "restoring_files",
            Self::Finalising => "finalising",
        }
    }

    /// The percentage this stage starts at, inclusive.
    #[must_use]
    pub fn start_percent(self) -> u32 {
        match self {
            Self::Verifying => 0,
            Self::Downloading => 10,
            Self::RestoringDatabases => 30,
            Self::RestoringFiles => 70,
            Self::Finalising => 95,
        }
    }

    /// The percentage this stage ends at — the start of the next one, and 99
    /// for the last, because 100 is the terminal message's alone.
    #[must_use]
    pub fn end_percent(self) -> u32 {
        match self {
            Self::Verifying => 10,
            Self::Downloading => 30,
            Self::RestoringDatabases => 70,
            Self::RestoringFiles => 95,
            Self::Finalising => 99,
        }
    }

    /// Interpolates a percentage inside this stage's span from work done.
    ///
    /// `done` out of `total`, mapped onto [`Self::start_percent`]‥
    /// [`Self::end_percent`]. A `total` of zero answers the span's start rather
    /// than dividing: no work means no progress through it.
    ///
    /// This exists so that no call site writes a percentage down. A literal
    /// percentage keeps being emitted after the work behind it has stopped
    /// happening, which is how an account-deletion cascade reported 10/50/90
    /// over rows it had not touched, and nothing could see it.
    #[must_use]
    pub fn percent_through(self, done: u64, total: u64) -> u32 {
        let start = u64::from(self.start_percent());
        let end = u64::from(self.end_percent());
        if total == 0 {
            return self.start_percent();
        }

        let span = end - start;
        // Multiplied before dividing, so the answer is not rounded to zero for
        // every step of a large total.
        let through = start + (span * done.min(total)) / total;
        u32::try_from(through).unwrap_or(self.end_percent())
    }
}

#[cfg(test)]
#[path = "../../tests/backup/restore_stage_tests.rs"]
mod tests;
