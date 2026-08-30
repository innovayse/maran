//! Reading this process's own uid without pulling in a libc binding.

/// Path whose owner is, by definition, this process's effective uid.
const SELF_PROC: &str = "/proc/self";

/// The effective uid of the running process.
///
/// Read from `/proc/self`'s owner rather than through a `getuid` binding: the
/// agent's `unsafe` budget is spent only where a syscall wrapper is genuinely
/// unavoidable (rules/rust.md), and this is not one of those places.
///
/// # Errors
///
/// Returns the underlying error when `/proc` is not mounted, which on Linux
/// means the process cannot meaningfully continue.
pub fn current_uid() -> Result<u32, std::io::Error> {
    use std::os::unix::fs::MetadataExt;

    Ok(std::fs::metadata(SELF_PROC)?.uid())
}
