//! The named stages a backup creation reports progress under.

/// One stage of a backup creation, and the span of the overall percentage it
/// owns.
///
/// The spans are FIXED and are part of the operation's contract with whoever
/// watches it: `dumping_databases` owns 0→40, `archiving_files` owns 40→80,
/// `uploading` owns 80→99, and 100 belongs to the terminal message alone. A
/// fixed span is what makes a stall legible — an operation stuck at 62% is
/// stuck while archiving, which is a place an operator can go and look at,
/// rather than a number that means whatever the last author felt.
///
/// The stage names are the wire names, lowercase with underscores, because the
/// service layer copies them into the stream verbatim and a second spelling
/// living in that layer is a second thing to keep in step.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BackupStage {
    /// One SQL dump is being taken per database the catalog named.
    DumpingDatabases,

    /// `tar` is reading the account's home and writing the artifact.
    ArchivingFiles,

    /// The finished artifact is being sent to a remote destination.
    ///
    /// A creation whose destination is local never reports this stage: there is
    /// no upload, and reporting one would be a claim about work that did not
    /// happen.
    Uploading,
}

impl BackupStage {
    /// The stage's wire name.
    #[must_use]
    pub fn as_str(self) -> &'static str {
        match self {
            Self::DumpingDatabases => "dumping_databases",
            Self::ArchivingFiles => "archiving_files",
            Self::Uploading => "uploading",
        }
    }

    /// The percentage this stage starts at, inclusive.
    #[must_use]
    pub fn start_percent(self) -> u32 {
        match self {
            Self::DumpingDatabases => 0,
            Self::ArchivingFiles => 40,
            Self::Uploading => 80,
        }
    }

    /// The percentage this stage ends at — the start of the next one, and 99
    /// for the last, because 100 is the terminal message's alone.
    #[must_use]
    pub fn end_percent(self) -> u32 {
        match self {
            Self::DumpingDatabases => 40,
            Self::ArchivingFiles => 80,
            Self::Uploading => 99,
        }
    }

    /// Interpolates a percentage inside this stage's span from work done.
    ///
    /// `done` out of `total`, mapped onto [`Self::start_percent`]‥
    /// [`Self::end_percent`]. A `total` of zero answers the span's start rather
    /// than dividing: no work means no progress through it.
    ///
    /// This exists so that no call site writes a percentage down. A literal
    /// percentage is a number that keeps being emitted after the work behind it
    /// has stopped happening, which is how an account-deletion cascade reported
    /// 10/50/90 over rows it had not touched, and nothing could see it.
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
#[path = "../../tests/backup/backup_stage_tests.rs"]
mod tests;
