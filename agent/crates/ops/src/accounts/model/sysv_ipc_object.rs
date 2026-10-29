//! One SysV IPC object as `/proc/sysvipc` lists it.

use crate::accounts::model::sysv_ipc_kind::SysvIpcKind;

/// One shared-memory segment, message queue or semaphore set, reduced to what
/// deciding its removal needs.
///
/// The kernel lists more (key, permissions, sizes, times); none of it matters
/// here. What matters is who owns the object (`uid`) and who created it
/// (`cuid`): either one being the account's uid makes the object the account's,
/// because the creator can have handed ownership away and the object still
/// carries the creator's uid.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SysvIpcObject {
    /// Which table the object was listed in.
    pub kind: SysvIpcKind,
    /// The kernel's identifier for the object (`shmid`, `msqid` or `semid`),
    /// the value `ipcrm` takes.
    pub id: u32,
    /// The uid that owns the object now.
    pub uid: u32,
    /// The uid that created the object.
    pub cuid: u32,
}
