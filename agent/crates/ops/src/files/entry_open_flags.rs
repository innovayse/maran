//! The flags an entry of a customer's tree is opened with before it is judged.

/// Flags every entry opened by name inside a customer's tree gets.
///
/// `O_NOFOLLOW` refuses a symlink at the name, `O_NONBLOCK` makes a FIFO the
/// account left there return instead of blocking the process in the kernel
/// forever (a child blocked there outlives its patience and costs a pool thread
/// per request), and `O_CLOEXEC` keeps the descriptor out of anything spawned
/// later. The caller still `fstat`s the result: these flags say which inode was
/// opened, not that it is the kind of thing the caller came for.
pub(crate) const ENTRY_OPEN_FLAGS: libc::c_int =
    libc::O_RDONLY | libc::O_NOFOLLOW | libc::O_NONBLOCK | libc::O_CLOEXEC;
