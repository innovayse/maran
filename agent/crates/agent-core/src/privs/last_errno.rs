//! Reading the calling thread's `errno`.

/// The current thread's `errno`.
///
/// Read through `std::io::Error` rather than through a second `unsafe` block: the
/// standard library already owns a correct, per-thread reader for it, and this
/// module's `unsafe` budget is spent only where nothing safe exists.
pub(super) fn last_errno() -> i32 {
    std::io::Error::last_os_error().raw_os_error().unwrap_or(0)
}
