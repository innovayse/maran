//! Binding the agent's unix socket with the permissions the panel relies on.

use std::os::unix::fs::{DirBuilderExt, PermissionsExt};
use std::path::Path;

use tokio::net::UnixListener;

/// Permissions the socket is created with: owner and group only.
///
/// The agent runs as root, so the socket's group is what lets the panel user
/// reach it; world access would hand the panel's full command set to every local
/// account on the machine.
const SOCKET_MODE: u32 = 0o660;

/// Permissions the socket's directory is created with: owner and group traversal
/// only. In production systemd owns `/run/maran` and applies the same mode; this
/// covers a developer run, where the directory is created here.
const DIRECTORY_MODE: u32 = 0o750;

/// Prepares the socket's directory, removes a stale socket and binds `socket_path`
/// at mode `0660`.
///
/// # Errors
///
/// Returns the I/O error when the directory cannot be created, a stale socket
/// cannot be removed, the bind fails or the mode cannot be applied.
pub(crate) fn bind_socket(socket_path: &Path) -> std::io::Result<UnixListener> {
    // The directory is created before the socket and no wider than the socket
    // itself, because binding is not atomic with respect to permissions: the
    // kernel creates the socket using the process umask — typically world
    // readable — and only the next statement narrows it. A directory nobody else
    // can traverse closes that window without a umask call, which would mean an
    // `unsafe` libc wrapper outside the one module allowed to hold them
    // (rules/rust.md).
    if let Some(directory) = socket_path.parent() {
        std::fs::DirBuilder::new()
            .recursive(true)
            .mode(DIRECTORY_MODE)
            .create(directory)?;
    }

    // A socket file left by a killed process would make bind fail with
    // "address in use", so the stale entry is removed rather than reported.
    if socket_path.exists() {
        std::fs::remove_file(socket_path)?;
    }

    let listener = UnixListener::bind(socket_path)?;
    std::fs::set_permissions(socket_path, std::fs::Permissions::from_mode(SOCKET_MODE))?;
    Ok(listener)
}
