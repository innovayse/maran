//! The three kinds of SysV IPC object the kernel lists under `/proc/sysvipc`.

/// Which `/proc/sysvipc` table is being asked about.
///
/// A closed set: the kernel has exactly these three SysV object kinds, and each
/// has its own table and its own `ipcrm` selector.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SysvIpcKind {
    /// Shared-memory segments (`/proc/sysvipc/shm`).
    Shm,
    /// Message queues (`/proc/sysvipc/msg`).
    Msg,
    /// Semaphore sets (`/proc/sysvipc/sem`).
    Sem,
}

impl SysvIpcKind {
    /// The file name of this kind's table under `/proc/sysvipc`.
    #[must_use]
    pub const fn table_name(self) -> &'static str {
        match self {
            Self::Shm => "shm",
            Self::Msg => "msg",
            Self::Sem => "sem",
        }
    }
}
