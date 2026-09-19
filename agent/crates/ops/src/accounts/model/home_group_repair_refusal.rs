//! Why one account's home was left exactly as it was found.

use std::fmt::{Display, Formatter, Result as FmtResult};

/// The reason a `RepairHomeGroups` pass refused to touch an account's home.
///
/// **A refusal is the answer for anything the repair cannot classify with
/// certainty.** This runs as root and its one tool is `chgrp`, so a home that
/// is not exactly what [`super::super::AccountOperations::create`] would have
/// made is reported to an operator instead of being guessed at. The
/// alternative — re-grouping whatever sits at the expected path — is a
/// privilege problem the moment that path is not the account's own directory:
/// a symlink planted there, a home moved onto its own mount, a directory that
/// belongs to a different uid entirely.
///
/// Every variant is reported per account, with the account's name and the
/// path examined, so "left alone" is something an operator reads rather than
/// assumes.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[non_exhaustive]
pub enum HomeGroupRepairRefusal {
    /// The system user's recorded home is not `<home root>/<account>` — the
    /// one path this agent has ever created an account's home at.
    ///
    /// An account whose home was moved, symlinked from elsewhere at the
    /// account level, or created by hand at a different path is not a case
    /// this repair reasons about at all: acting on "the path the account
    /// happens to record" rather than "the path this agent would have built"
    /// is exactly the kind of unchecked trust a root process must not extend.
    HomeNotAtExpectedPath,

    /// The path this agent would have created the home at does not exist.
    ///
    /// Nothing to `chgrp`. Reported rather than silently skipped, because an
    /// account with no home at all cannot serve anything either, and that is
    /// a different problem from the one this repair fixes but one an operator
    /// still needs to see.
    HomeMissing,

    /// The path is a symlink rather than the home directory itself.
    ///
    /// `chgrp --no-dereference` would re-group the LINK, not whatever it
    /// points at, which does nothing useful — but a root process that got
    /// this far already found something planted where a home should be, and
    /// that is worth an operator's attention on its own.
    Symlink,

    /// The path exists and is not a symlink, but is not a directory either.
    NotADirectory,

    /// The path sits on a different filesystem than the account home root.
    ///
    /// Every home this agent creates is a plain directory under the home
    /// root's own mount. A path that is its own mount point — a bind mount,
    /// network storage, anything an operator attached after the fact — is not
    /// a shape `useradd --create-home` produces, and re-grouping it applies a
    /// permission change to whatever is mounted there rather than to a
    /// directory this agent owns.
    DifferentMount,

    /// The path's owning uid is not the account's own uid.
    ///
    /// `useradd --create-home` makes an account own its own home; a home
    /// owned by a different uid was not left that way by this agent, and a
    /// root process re-grouping a directory it does not know the origin of
    /// is a privilege problem, not an untidiness.
    OwnerMismatch,
}

impl Display for HomeGroupRepairRefusal {
    /// Writes the operator-facing English the report and the log line carry.
    fn fmt(&self, formatter: &mut Formatter<'_>) -> FmtResult {
        let reason = match self {
            Self::HomeNotAtExpectedPath => {
                "the account's recorded home is not the path this agent creates homes at"
            }
            Self::HomeMissing => {
                "the path this agent would have created the home at does not exist"
            }
            Self::Symlink => "the path is a symlink, not the home directory itself",
            Self::NotADirectory => "the path exists but is not a directory",
            Self::DifferentMount => "the path sits on a different filesystem than the home root",
            Self::OwnerMismatch => "the path is not owned by the account's own uid",
        };

        formatter.write_str(reason)
    }
}
