//! The two states one listed backup can be in, as a shape rather than a flag.

use serde::{Deserialize, Serialize};

use crate::backup::model::readable_backup::ReadableBackup;
use crate::backup::model::unreadable_reason::UnreadableReason;

/// Whether a backup's sidecar could be read, and — in exactly one of the two
/// cases — what it said.
///
/// This is an enum and not a `bool` beside a handful of `Option`s because the
/// two are not the same promise. A flag can disagree with the fields next to
/// it; a variant cannot. `serde` builds this type from a file on disk that
/// nothing in this agent wrote, and a file is not obliged to be consistent —
/// so the only invariant worth stating here is one the parser is structurally
/// unable to break. [`Self::Readable`] carries a whole
/// [`ReadableBackup`]; there is no arrangement of JSON that produces a
/// readable state with no manifest or no digest, because there is no field to
/// leave out.
///
/// The cost is real and is paid deliberately: the on-disk sidecar's shape
/// changes (the `readable` boolean and the three flat, optional fields become
/// one tagged object), and the wire message this becomes will be a `oneof`
/// rather than a `bool` and four fields. Both are cheap now and permanent
/// later, which is why the change is made before the contract is frozen and
/// not after.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum BackupState {
    /// The sidecar was read, parsed, and understood at the current version,
    /// and this is what it described.
    Readable(ReadableBackup),

    /// The sidecar could not be trusted, for the reason carried here. The
    /// artifact still exists and is still listed — see
    /// [`BackupSummary`](crate::backup::model::backup_summary::BackupSummary)
    /// for why an entry nobody lists is an entry retention will never prune.
    Unreadable(UnreadableReason),
}
