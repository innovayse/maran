//! Which of the swap's intermediate states the filesystem is actually in.

use std::path::Path;

/// The state a marked swap is observed in, decided from inodes.
///
/// Every variant is one row of the table in
/// `docs/superpowers/notes/2026-09-09-restore-interruption-recovery-threat-note.md`,
/// and the enum exists so that the reconciliation is a `match` over a closed set
/// rather than a ladder of `if`s that can silently have no arm for a state the
/// disk can really be in.
///
/// Two renames cannot be atomic with respect to each other. This type is how
/// that window is made **recoverable** instead of being pretended away: the
/// window is enumerable, so every point in it has a decided answer.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SwapState {
    /// Marker written, nothing moved: the staging tree is there and the live
    /// home is still the live home.
    ///
    /// Reached by a kill after the marker and before the first rename — and
    /// also by the in-process reversal succeeding while the marker's removal did
    /// not. The two are indistinguishable from the disk and want the same
    /// answer, which is why they share a variant rather than being guessed
    /// apart.
    NotStarted,

    /// **The window.** The home is parked, the staging tree is waiting, and
    /// `/home/<account>` does not exist. This is the state F-2 is about.
    Interrupted,

    /// The second rename happened; the parked tree is still there and the
    /// ownership may or may not have been re-applied.
    Swapped,

    /// The swap and the finalisation both completed; only the marker's own
    /// removal did not.
    ///
    /// **The state a reconciler must not "recover".** A reconciler that acts on
    /// healthy state is worse than none, and this variant is what makes doing
    /// nothing an explicit decision rather than a missing branch.
    Completed,

    /// The home is present AND the previous tree is parked AND the staging tree
    /// is still there.
    ///
    /// Unreachable from `swap_home`, whose first `rename` makes the home absent
    /// in the same instant it makes the parked tree appear. Observing it means
    /// something outside this operation re-created the home — an operator, or an
    /// account re-created by `useradd`. There is no safe automatic answer, so
    /// there is a variant for it and the answer is a human's.
    HomeAndParkedBoth,

    /// Neither the home, nor the parked tree, nor the staging tree is there.
    /// Nothing was left to recover with.
    NothingLeft,

    /// The home is absent and the PARKED tree is the only thing left: the
    /// staging tree is gone.
    ///
    /// Reached when `perform` returned an error after the marker was written and
    /// after the home had been parked, and `restore_in`'s unconditional cleanup
    /// then removed the staging tree on the way out. There is nothing to finish
    /// forward with — the replacement home no longer exists — and the customer's
    /// own home is one rename away. **Roll back.**
    ParkedOnly,

    /// The home is absent and the STAGING tree is the only thing left.
    ///
    /// Not reachable from `swap_home`, whose second rename leaves the home
    /// present before anything removes the parked tree. If it is ever observed,
    /// the account has no home at all and the staging tree is a verified,
    /// fully-extracted copy of the backup's — so putting it in place is strictly
    /// better than leaving the account with nothing. **Finish forward.**
    StagingOnly,

    /// One of the three names is not a directory — a symlink, a regular file, a
    /// device. Nothing is touched.
    ///
    /// `symlink_metadata` is what distinguishes this: a symlink at any of these
    /// names, followed by a root process, is an arbitrary write at a path
    /// somebody else chose.
    NotDirectories,
}

impl SwapState {
    /// Decides the state from what the three paths actually are.
    ///
    /// Every observation is `symlink_metadata`, never `metadata` and never
    /// [`Path::exists`]: both of those follow links, and both answer `false` for
    /// a path that exists but cannot be stat'd, which would turn a permissions
    /// problem into "the home is gone" and then into a `rename` over it.
    #[must_use]
    pub fn classify(home: &Path, previous: &Path, staging: &Path) -> Self {
        let home = Presence::of(home);
        let previous = Presence::of(previous);
        let staging = Presence::of(staging);

        if home == Presence::Other || previous == Presence::Other || staging == Presence::Other {
            return Self::NotDirectories;
        }

        match (
            home == Presence::Directory,
            previous == Presence::Directory,
            staging == Presence::Directory,
        ) {
            (true, false, true) => Self::NotStarted,
            (false, true, true) => Self::Interrupted,
            (true, true, false) => Self::Swapped,
            (true, false, false) => Self::Completed,
            (true, true, true) => Self::HomeAndParkedBoth,
            (false, false, false) => Self::NothingLeft,
            (false, true, false) => Self::ParkedOnly,
            (false, false, true) => Self::StagingOnly,
        }
    }
}

/// What one of the three names is, as far as a recovery is allowed to care.
///
/// Three states and not a boolean, because "absent" and "present but not a
/// directory" must not collapse into one answer: the first is a step of the swap
/// that did not happen, and the second is something nobody in this code put
/// there.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Presence {
    /// A directory.
    Directory,
    /// Nothing at this name, or nothing this process may stat.
    Absent,
    /// Something that is not a directory — a symlink, a file, a device.
    Other,
}

impl Presence {
    /// Reads `path` without following a link at its final component.
    fn of(path: &Path) -> Self {
        match path.symlink_metadata() {
            Ok(metadata) if metadata.is_dir() => Self::Directory,
            Ok(_) => Self::Other,
            Err(_) => Self::Absent,
        }
    }
}

#[cfg(test)]
#[path = "../../tests/backup/swap_state_tests.rs"]
mod tests;
