//! Server assembly: socket preparation, peer-cred enforcement, service registry.

use std::os::unix::fs::{DirBuilderExt, PermissionsExt};
use std::path::Path;

use tokio::net::UnixListener;
use tokio_stream::wrappers::UnixListenerStream;
use tonic::transport::Server;

use crate::error::StartupError;
use crate::peercred::{PeerGuard, PeerPolicy};
use crate::proto::system_service_server::SystemServiceServer;
use crate::services::system::SystemServiceImpl;

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

/// Binds `socket_path` and serves the contract until the process is stopped.
///
/// # Errors
///
/// Returns [`StartupError`] when the socket cannot be prepared, the host is
/// unsupported, or the server terminates abnormally.
pub async fn serve(socket_path: &Path, policy: PeerPolicy) -> Result<(), StartupError> {
    // Detection first: refusing an unsupported host before the socket exists
    // means the panel sees "agent absent" rather than an agent that accepts
    // connections and then fails every operation.
    let distro = maran_distro::detect()?;

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

    tracing::info!(
        socket = %socket_path.display(),
        distro = %distro.id,
        version = %distro.version_id,
        "agent listening"
    );

    Server::builder()
        .add_service(SystemServiceServer::with_interceptor(
            SystemServiceImpl::new(distro),
            PeerGuard::new(policy),
        ))
        .serve_with_incoming(UnixListenerStream::new(listener))
        .await?;

    Ok(())
}
