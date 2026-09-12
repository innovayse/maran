//! The document a restore writes before it moves an account's home.

use serde::{Deserialize, Serialize};

use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::name::AccountName;

/// The version this agent writes and the only one it reads back.
///
/// Present for the reason the backup manifest carries one: a document read by a
/// FUTURE build of this daemon must be refusable by version rather than
/// misread field by field. A marker whose version this build does not know is
/// refused whole, and the reconciliation leaves everything it named alone —
/// which is the safe direction, because the alternative is a root `rename` made
/// on a guess.
pub const RESTORE_MARKER_VERSION: u32 = 1;

/// What a restore records, immediately before the first of its two renames, so
/// that a later process can finish the swap the killed one started.
///
/// # Why a document and not the directory layout
///
/// The layout alone would be enough to notice that *a* swap was interrupted:
/// a `<account>.previous.<id>` tree beside an absent `/home/<account>` says so.
/// It is not enough to say **whose**, and the only way to get the account out of
/// the layout is to parse it out of the directory's name. That is the shape this
/// repository has already been burned by, and it is refused here on principle:
/// a root process deciding which home to `rename` a tree into may not derive the
/// answer from a string it found on the disk. The account arrives here as an
/// [`AccountName`] that the operation had already validated, is written out, and
/// is put back through [`AccountName::parse`] on the way in. Everything the
/// reconciliation then touches is a path **it computes itself** from
/// `AgentPaths` and those two validated tokens.
///
/// # Why there are no paths in it
///
/// Deliberately none. A path in this file would be a string a `rename` could be
/// pointed at by whoever could write the file — and while nothing unprivileged
/// can write into the staging root today (`/home` is root's `0755`, the root
/// itself root's `0711`), a document that CANNOT name a destination stays safe
/// if that ever stops being true. The three directories of a swap are derived,
/// not read.
///
/// # Why the ownership is in it
///
/// Because step 8 has to be finishable by a process that never saw the request.
/// The uid is the account's, the gid is the WEB SERVER's — resolved by the
/// caller from the distro adapter, and not something a reconciliation could work
/// out from the parked tree, whose group is whatever the previous restore left.
/// A recovery that guessed the group would hand every site on the account a
/// silent 403, which is the exact failure `finalise` documents.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct RestoreMarker {
    /// The document's version. See [`RESTORE_MARKER_VERSION`].
    pub version: u32,

    /// The account whose home is being swapped, as the operation validated it.
    ///
    /// A `String` in the document and an [`AccountName`] everywhere else: what
    /// is on the disk is bytes until it has been through the validator, and
    /// giving the field the validated type would have serde construct one
    /// without ever running it.
    pub account: String,

    /// The backup being restored, as the operation validated it.
    pub backup_id: String,

    /// The uid the restored home root must end up owned by — the account's own.
    pub owner_uid: u32,

    /// The gid the restored home root must end up owned by — the web server's,
    /// so it can traverse the home to serve the account's sites.
    pub home_gid: u32,

    /// The mode the restored home root must carry, `0750` in every build that
    /// has written one of these.
    pub home_mode: u32,
}

impl RestoreMarker {
    /// The account this marker names, or `None` because the bytes on the disk
    /// are not a name this agent would ever have written.
    #[must_use]
    pub fn account(&self) -> Option<AccountName> {
        AccountName::parse(&self.account).ok()
    }

    /// The backup this marker names, or `None` for the same reason.
    #[must_use]
    pub fn backup_id(&self) -> Option<BackupId> {
        BackupId::parse(&self.backup_id).ok()
    }
}
